// Uma sala = uma partida. O servidor é a autoridade: roda a simulação a 60 Hz
// e manda um retrato do mundo a 20 Hz pra todo mundo.
//
// O ciclo da campanha:
//   lobby → construção → fase → intervalo (upgrade) → construção → fase → … → fim
//
// A Sucata é do esquadrão, não de cada um: entra ao limpar a fase e sai no
// Modo Construção, comprando defesa pra águia. Os upgrades do intervalo, esses
// sim, são de cada jogador.

import { performance } from 'node:perf_hooks';
import {
  TICK_MS, SNAPSHOT_EVERY, MAX_PLAYERS, PLAYER_LIVES, PLAYER_COLORS,
  PLAYER_SPAWNS, SHIELD_TICKS, TEAM, CLASS_IDS, CAMPAIGN, SKIN_IDS,
  UPGRADES, UPGRADE_BY_ID, UPGRADE_CARDS, INTERMISSION_MS, STAGE_BONUS_LIVES,
  FORTIFICACOES, FORT_BY_ID, CONSTRUCAO_MS, RECONECTA_MS, SUCATA, MOEDA,
  PONTOS_INIMIGO,
} from '../shared/constants.js';
import { randomUUID } from 'node:crypto';
import { MAPS } from '../shared/maps.js';
import { createWorld, createTank, stepWorld, montarFortificacoes } from '../shared/sim.js';
import { enemyTurn, spawnEnemy, spawnBoss } from './ai.js';

const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // sem I/O/0/1 pra não confundir

export function makeCode(taken) {
  for (let tries = 0; tries < 500; tries++) {
    let c = '';
    for (let i = 0; i < 4; i++) c += ALPHABET[Math.floor(Math.random() * ALPHABET.length)];
    if (!taken.has(c)) return c;
  }
  throw new Error('sem códigos livres');
}

// Upgrades que não fazem sentido pegar duas vezes.
const UNICOS = new Set(['nucleo']);

function sortearCartas(jaTem) {
  const pool = UPGRADES.filter((u) => !(UNICOS.has(u.id) && jaTem.includes(u.id)));
  const out = [];
  const usados = new Set();
  while (out.length < Math.min(UPGRADE_CARDS, pool.length)) {
    const u = pool[Math.floor(Math.random() * pool.length)];
    if (usados.has(u.id)) continue;
    usados.add(u.id);
    out.push({ id: u.id, name: u.name, icon: u.icon, desc: u.desc });
  }
  return out;
}

// Quanto custa subir a fortificação `id` do nível que ela está. null = no teto.
export function custoDoProximo(forts, id) {
  const f = FORT_BY_ID[id];
  if (!f) return null;
  const nivel = forts[id] | 0;
  return nivel >= f.niveis.length ? null : f.niveis[nivel].custo;
}

export class Room {
  constructor(code) {
    this.code = code;
    this.players = new Map();   // id -> { id, ws, name, cls, skin, tankId, ... }
    this.hostId = null;
    this.state = 'lobby';       // lobby | construcao | playing | intervalo | ended
    this.mode = 'campanha';     // campanha | treino
    this.stage = 0;
    this.world = null;
    this.timer = null;
    this.faseTimer = null;      // conta o tempo da construção / do intervalo
    this.paused = false;
    this.sucata = 0;            // caixa do esquadrão
    this.forts = {};            // id da fortificação -> nível comprado
    this.pendingDirty = [];
    this.pendingFx = [];
    this.lastTouch = Date.now();
  }

  get size() { return this.players.size; }
  get empty() { return this.players.size === 0; }
  // Quem está de fato conectado agora. Vaga guardada não conta.
  get online() { return [...this.players.values()].filter((p) => !p.offline).length; }
  // No treino a "campanha" tem uma fase só — e ela é sempre a nº 1, mesmo que
  // o mapa escolhido seja o quarto. Senão o HUD escreve "4/1".
  get totalFases() { return this.mode === 'campanha' ? CAMPAIGN.length : 1; }
  get faseVisivel() { return this.mode === 'campanha' ? this.stage : 0; }

  // ------------------------------------------------------------ jogadores

  // Dá pra entrar agora? Perguntado ANTES de o jogador sair da sala em que
  // está, pra uma recusa não deixar ninguém no limbo.
  podeEntrar() {
    return this.players.size < MAX_PLAYERS && (this.state === 'lobby' || this.state === 'ended');
  }

  motivoRecusa() {
    if (this.players.size >= MAX_PLAYERS) return 'Sala cheia (4 jogadores).';
    return 'Partida já começou. Peça pro host reiniciar.';
  }

  // Só do lobby (ou do fim) se recomeça. Um "iniciar" durante o Modo
  // Construção chamaria begin() de novo e apagaria a Sucata e as
  // fortificações que o esquadrão tinha acabado de comprar.
  podeIniciar() {
    return this.state === 'lobby' || this.state === 'ended';
  }

  addPlayer(id, ws, name, cls, skin) {
    if (!this.podeEntrar()) return { ok: false, msg: this.motivoRecusa() };

    const used = new Set([...this.players.values()].map((p) => p.slot));
    let slot = 0;
    while (used.has(slot)) slot++;

    const player = {
      id, ws, slot,
      // Cracha da vaga: é com ele que quem cai volta pro mesmo lugar, com os
      // pontos, as vidas e os upgrades que já tinha.
      token: randomUUID(),
      offline: false,
      purga: null,
      // String() antes de qualquer coisa: o nome vem da rede e pode ser
      // número, objeto ou array. Sem isto, um `.trim()` derruba o processo.
      name: String(name ?? '').trim().slice(0, 12) || `Piloto ${slot + 1}`,
      cls: CLASS_IDS.includes(cls) ? cls : 'assalto',
      skin: SKIN_IDS.includes(skin) ? skin : 'liso',
      tankId: null,
      input: { dir: null, move: false, fire: false },
      usarHab: false,
      // progresso da campanha
      lives: PLAYER_LIVES,
      vidasNoInicio: PLAYER_LIVES,
      score: 0,
      kills: 0,
      upgrades: [],
      cartas: [],
      pick: null,
      pronto: false,
    };
    this.players.set(id, player);
    if (!this.hostId) this.hostId = id;
    this.lastTouch = Date.now();
    return { ok: true, player };
  }

  // Alguém caiu. No lobby não tem o que guardar: sai e pronto. Com a partida
  // em andamento a vaga fica de pé por um minuto, com pontos, vidas, upgrades
  // e o tanque no lugar — é o que permite voltar de uma queda de Wi-Fi.
  removePlayer(id) {
    const p = this.players.get(id);
    if (!p) return;

    if (this.state === 'lobby' || p.offline) return this.purgar(id);

    p.offline = true;
    p.ws = null;
    p.input = { dir: null, move: false, fire: false };
    p.usarHab = false;
    p.pronto = true;                  // não trava a obra nem o intervalo
    if (!p.pick) p.pick = null;
    clearTimeout(p.purga);
    p.purga = setTimeout(() => this.purgar(id), RECONECTA_MS);

    this.passarHost(id);
    this.avisarEstado();
    this.lastTouch = Date.now();
  }

  // Some de vez: a vaga não voltou no tempo, ou a sala está no lobby.
  purgar(id) {
    const p = this.players.get(id);
    if (!p) return;
    clearTimeout(p.purga);
    this.players.delete(id);
    if (this.world) {
      const tank = this.world.tanks.find((t) => t.id === p.tankId);
      if (tank) { tank.alive = false; tank.lives = 0; tank.left = true; }
    }
    this.passarHost(id);
    if (this.players.size === 0) { this.stop(); this.state = 'lobby'; this.world = null; }
    else this.avisarEstado();
    this.lastTouch = Date.now();
  }

  passarHost(id) {
    if (this.hostId !== id) return;
    // prefere quem está online pra assumir
    const online = [...this.players.values()].find((p) => !p.offline);
    this.hostId = (online ?? this.players.values().next().value)?.id ?? null;
    // Só o host pausa. Se ele sai pausado, a sala congelaria até alguém
    // descobrir que agora é host e apertar P.
    if (this.paused) { this.paused = false; this.broadcast({ t: 'pausa', on: false }); }
  }

  avisarEstado() {
    if (this.state === 'lobby') this.sendLobby();
    else if (this.state === 'construcao') { this.sendConstrucao(); this.talvezComecar(); }
    else if (this.state === 'intervalo') { this.sendEscolhas(); this.talvezSeguir(); }
    else this.sendLobby();
  }

  // Volta pra vaga guardada. Devolve o jogador ou null se o cracha não bate.
  reconectar(token, ws, novoId) {
    const p = [...this.players.values()].find((x) => x.token === token && x.offline);
    if (!p) return null;

    clearTimeout(p.purga);
    p.purga = null;
    this.players.delete(p.id);
    p.id = novoId;
    p.ws = ws;
    p.offline = false;
    p.pronto = this.state === 'construcao' ? false : p.pronto;
    this.players.set(novoId, p);
    if (!this.hostId || !this.players.has(this.hostId)) this.hostId = novoId;
    this.lastTouch = Date.now();
    return p;
  }

  // Manda pra quem acabou de voltar exatamente a tela em que a sala está.
  reenviarEstado(p) {
    if (this.state === 'construcao') return this.sendConstrucao();
    if (this.state === 'intervalo') {
      if (p.ultimoInter) this.sendTo(p, p.ultimoInter);
      this.sendEscolhas();
      return;
    }
    if (this.state === 'ended') {
      if (this.ultimoFim) this.sendTo(p, this.ultimoFim);
      return;
    }
    if (this.state === 'playing' && this.world) {
      const fase = this.faseAtual();
      const map = MAPS[fase.map] ?? MAPS[0];
      // `tiles` vai no estado ATUAL do campo, com os tijolos já quebrados.
      this.sendTo(p, {
        t: 'start',
        voltou: true,
        stage: this.faseVisivel,
        totalFases: this.totalFases,
        modo: this.mode,
        mapId: map.id,
        mapName: map.name,
        mapTag: map.tag,
        chefe: this.world.wantBoss,
        inimigos: this.world.enemiesLeft,
        forts: { ...this.forts },
        tiles: Array.from(this.world.tiles),
      });
      return;
    }
    this.sendLobby();
  }

  // ------------------------------------------------------------ ciclo de vida

  // Zera o progresso e manda o esquadrão pro Modo Construção da primeira fase.
  begin() {
    for (const p of this.players.values()) {
      p.lives = PLAYER_LIVES;
      p.score = 0;
      p.kills = 0;
      p.upgrades = [];
      p.pick = null;
      p.cartas = [];
    }
    this.sucata = 0;
    this.forts = {};
    if (this.mode === 'campanha') this.stage = 0;
    this.abrirConstrucao();
  }

  // ---------------------------------------------------- modo construção

  abrirConstrucao() {
    this.stop();
    this.state = 'construcao';
    this.paused = false;
    for (const p of this.players.values()) p.pronto = false;
    this.sendConstrucao();
    this.faseTimer = setTimeout(() => { this.faseTimer = null; this.iniciarFase(); }, CONSTRUCAO_MS);
  }

  sendConstrucao() {
    const mapa = MAPS[this.faseAtual().map] ?? MAPS[0];
    this.broadcast({
      t: 'build',
      stage: this.faseVisivel,
      totalFases: this.totalFases,
      mapName: mapa.name,
      mapTag: mapa.tag,
      chefe: !!this.faseAtual().boss,
      sucata: this.sucata,
      moeda: MOEDA,
      segundos: Math.round(CONSTRUCAO_MS / 1000),
      forts: FORTIFICACOES.map((f) => ({
        id: f.id, name: f.name, icon: f.icon, resumo: f.resumo,
        nivel: this.forts[f.id] | 0,
        max: f.niveis.length,
        niveis: f.niveis,
        custo: custoDoProximo(this.forts, f.id),
      })),
      prontos: [...this.players.values()].map((p) => ({
        id: p.id, name: p.name, color: PLAYER_COLORS[p.slot], pronto: p.pronto, offline: p.offline,
      })),
    });
  }

  comprarFort(id, fortId) {
    if (this.state !== 'construcao') return { ok: false, msg: 'A obra já fechou.' };
    if (!this.players.has(id)) return { ok: false, msg: 'Você não está na sala.' };
    const f = FORT_BY_ID[fortId];
    if (!f) return { ok: false, msg: 'Essa fortificação não existe.' };
    const custo = custoDoProximo(this.forts, fortId);
    if (custo === null) return { ok: false, msg: `${f.name} já está no último nível.` };
    if (this.sucata < custo) return { ok: false, msg: `Falta ${MOEDA.nome.toLowerCase()}.` };

    this.sucata -= custo;
    this.forts[fortId] = (this.forts[fortId] | 0) + 1;
    this.sendConstrucao();
    return { ok: true };
  }

  prontoConstrucao(id) {
    const p = this.players.get(id);
    if (!p || this.state !== 'construcao') return;
    p.pronto = true;
    this.sendConstrucao();
    this.talvezComecar();
  }

  // Quem está offline não segura a sala: a vaga dele fica guardada, mas o
  // esquadrão que ficou não espera por um browser que já foi embora.
  talvezComecar() {
    if (this.state !== 'construcao' || this.online === 0) return;
    if ([...this.players.values()].some((p) => !p.offline && !p.pronto)) return;
    this.stop();
    this.iniciarFase();
  }

  iniciarFase() {
    if (this.empty) { this.state = 'lobby'; return; }
    this.start(this.stage);
  }

  faseAtual() {
    return this.mode === 'campanha'
      ? CAMPAIGN[Math.min(this.stage, CAMPAIGN.length - 1)]
      : { map: this.stage % MAPS.length, enemies: 20, alive: 4, every: 145, tier: 1, boss: false };
  }

  // ---------------------------------------------------- a fase em si

  start(stage = 0) {
    const total = this.mode === 'campanha' ? CAMPAIGN.length : MAPS.length;
    this.stage = ((stage % total) + total) % total;
    const fase = this.faseAtual();

    // Afrouxa o número de inimigos por fase. Existe pros testes conseguirem
    // limpar uma fase na hora e conferir o intervalo; fora deles fica quieto.
    const quantos = process.env.APOLLO_INIMIGOS_POR_FASE;
    const inimigos = quantos === undefined ? fase.enemies : Math.max(0, Number(quantos) | 0);

    const map = MAPS[fase.map] ?? MAPS[0];
    const world = createWorld(map);
    montarFortificacoes(world, this.forts);
    world.stage = this.stage;
    world.tier = fase.tier;
    world.aliveMax = fase.alive;
    world.spawnEvery = fase.every;
    world.wantBoss = !!fase.boss && inimigos > 0;
    world.toSpawn = inimigos;
    world.enemiesLeft = inimigos;
    world.enemySpawnCd = 90;

    for (const p of this.players.values()) {
      const spawn = PLAYER_SPAWNS[p.slot] ?? PLAYER_SPAWNS[0];
      const tank = createTank({
        id: `p${p.slot}`,
        name: p.name,
        kind: 'player',
        team: TEAM.PLAYER,
        cls: p.cls,
        skin: p.skin,
        color: PLAYER_COLORS[p.slot],
        x: spawn.x, y: spawn.y,
        dir: 0,
        lives: p.lives,
        score: p.score,
        shield: SHIELD_TICKS,
        upgrades: p.upgrades,
      });
      tank.slot = p.slot;
      world.tanks.push(tank);
      p.tankId = tank.id;
      p.input = { dir: null, move: false, fire: false };
      p.usarHab = false;
      p.vidasNoInicio = p.lives;
    }

    this.world = world;
    this.state = 'playing';
    this.paused = false;
    this.pendingDirty = [];
    this.pendingFx = [];
    this.broadcast({
      t: 'start',
      stage: this.faseVisivel,
      totalFases: this.totalFases,
      modo: this.mode,
      mapId: map.id,
      mapName: map.name,
      mapTag: map.tag,
      chefe: world.wantBoss,
      inimigos,
      forts: { ...this.forts },
      tiles: Array.from(world.tiles),
    });

    this.stop();
    this.acc = 0;
    this.lastPump = performance.now();
    // O relógio do Windows só acorda o setInterval a cada ~15,6 ms, então um
    // intervalo de 16,7 ms entregaria uns 36 ticks/s. Aqui a gente acorda mais
    // vezes e roda quantos passos o tempo real pedir — a velocidade do jogo
    // fica certa em qualquer sistema.
    this.timer = setInterval(() => this.pump(), 4);
  }

  pump() {
    const agora = performance.now();
    let dt = agora - this.lastPump;
    this.lastPump = agora;
    if (dt > 250) dt = 250;              // depois de um engasgo, não dispara rajada
    // Sala sem ninguém online fica parada esperando quem caiu voltar, em vez
    // de deixar a IA derrubar a águia com o time inteiro desconectado.
    if (this.paused || this.online === 0) { this.acc = 0; return; }
    this.acc += dt;
    let passos = 0;
    while (this.acc >= TICK_MS && passos < 8 && this.state === 'playing') {
      this.acc -= TICK_MS;
      this.tick();
      passos++;
    }
    if (passos === 8) this.acc = 0;

    // APOLLO_TICK_LOG=1 mostra a velocidade real da simulação — útil se o jogo
    // parecer lento em alguma máquina.
    if (process.env.APOLLO_TICK_LOG) {
      this.dbg ||= { pumps: 0, t0: agora };
      if (++this.dbg.pumps % 300 === 0) {
        const s = (agora - this.dbg.t0) / 1000;
        console.error(`[tick] ${(this.world.tick / s).toFixed(1)} ticks/s (alvo 60) · sala ${this.code}`);
      }
    }
  }

  pausar(id, on) {
    if (this.hostId !== id || this.state !== 'playing') return;
    this.paused = !!on;
    this.broadcast({ t: 'pausa', on: this.paused });
  }

  stop() {
    if (this.timer) { clearInterval(this.timer); this.timer = null; }
    if (this.faseTimer) { clearTimeout(this.faseTimer); this.faseTimer = null; }
  }

  backToLobby() {
    this.stop();
    this.state = 'lobby';
    this.world = null;
    this.paused = false;
    for (const p of this.players.values()) { p.tankId = null; p.pick = null; p.cartas = []; p.pronto = false; }
    this.sendLobby();
  }

  // ------------------------------------------------------------ simulação

  tick() {
    const world = this.world;
    if (!world || this.state !== 'playing') return;

    const inputs = {};
    for (const p of this.players.values()) {
      if (!p.tankId) continue;
      // A habilidade é um toque, não um estado: vale por um tick só.
      inputs[p.tankId] = p.usarHab ? { ...p.input, ab: true } : p.input;
      p.usarHab = false;
    }

    stepWorld(world, inputs, {
      onEnemyTurn: (w, ins) => {
        this.trySpawnEnemy(w);
        enemyTurn(w, ins);
      },
      onRespawn: (w, tank) => {
        const spawn = PLAYER_SPAWNS[tank.slot] ?? PLAYER_SPAWNS[0];
        tank.x = spawn.x; tank.y = spawn.y;
        tank.dir = 0; tank.alive = true; tank.moving = false;
        tank.hp = tank.hpMax; tank.shield = SHIELD_TICKS; tank.fireCd = 0;
        tank.dashT = 0; tank.slide = 0;
        w.fx.push({ k: 'spawn', x: spawn.x + 8, y: spawn.y + 8 });
      },
    });

    // Só mandamos estado a cada 3 ticks, então guardamos o que mudou nos ticks
    // do meio pra não perder tijolo quebrado nem explosão.
    if (world.dirty.length) this.pendingDirty.push(...world.dirty);
    if (world.fx.length) this.pendingFx.push(...world.fx);

    if (world.tick % SNAPSHOT_EVERY === 0) this.sendSnapshot();
    if (world.over) { this.sendSnapshot(); this.finish(); }
  }

  trySpawnEnemy(world) {
    if (world.toSpawn <= 0) return;
    const alive = world.tanks.filter((t) => t.kind === 'enemy' && t.alive).length;
    if (alive >= world.aliveMax) return;
    if (--world.enemySpawnCd > 0) return;

    // O chefe é sempre o último a entrar: primeiro a fase esvazia, depois ele.
    const ehChefe = world.wantBoss && world.toSpawn === 1;
    const nasceu = ehChefe ? spawnBoss(world) : spawnEnemy(world, world.tier);
    if (nasceu) {
      world.toSpawn--;
      world.enemySpawnCd = world.spawnEvery;
    } else {
      world.enemySpawnCd = 20; // ponto de nascimento ocupado, tenta de novo já já
    }
  }

  // ------------------------------------------------------------ fim de fase

  // Guarda no jogador o que a fase rendeu, pra próxima começar de onde parou.
  guardarProgresso(bonusVidas = 0) {
    const w = this.world;
    if (!w) return;
    for (const p of this.players.values()) {
      const t = w.tanks.find((x) => x.id === p.tankId);
      if (!t) continue;
      p.score = t.score;
      p.kills += t.kills;
      p.lives = Math.max(0, t.lives) + bonusVidas;
    }
  }

  // A conta da Sucata da fase, item por item, pro jogador ver de onde veio.
  contaDaFase() {
    const w = this.world;
    const abates = Object.values(w.abatesPorTipo).reduce((s, n) => s + n, 0);
    const semMortes = [...this.players.values()].every((p) => {
      const t = w.tanks.find((x) => x.id === p.tankId);
      return t ? t.lives >= p.vidasNoInicio : true;
    });
    const linhas = [
      { rotulo: 'Fase concluída', valor: SUCATA.base },
      { rotulo: `${abates} inimigos destruídos`, valor: SUCATA.porAbate * abates },
    ];
    if (!w.muroQuebrado) linhas.push({ rotulo: 'Muro da águia intacto', valor: SUCATA.aguiaIntacta });
    if (semMortes) linhas.push({ rotulo: 'Ninguém caiu', valor: SUCATA.semMortes });
    return { linhas, total: linhas.reduce((s, l) => s + l.valor, 0) };
  }

  // Placar por tipo de inimigo, como na tela de fim de fase do NES.
  porTipo() {
    const w = this.world;
    return Object.entries(w.abatesPorTipo)
      .map(([tipo, n]) => ({ tipo, n, pontos: n * (PONTOS_INIMIGO[tipo] ?? 100) }))
      .sort((a, b) => b.pontos - a.pontos);
  }

  placar() {
    const w = this.world;
    return [...this.players.values()]
      .map((p) => {
        const t = w?.tanks.find((x) => x.id === p.tankId);
        return {
          id: p.id,
          name: p.name,
          color: PLAYER_COLORS[p.slot],
          cls: p.cls,
          kills: p.kills + (t ? t.kills : 0),
          score: t ? t.score : p.score,
          lives: Math.max(0, t ? t.lives : p.lives),
          upgrades: p.upgrades.map((id) => UPGRADE_BY_ID[id]?.name).filter(Boolean),
        };
      })
      .sort((a, b) => b.score - a.score);
  }

  finish() {
    this.stop();
    const w = this.world;
    const venceu = w.result === 'vitoria';
    const temMais = this.mode === 'campanha' && this.stage + 1 < CAMPAIGN.length;

    if (venceu) {
      const conta = this.contaDaFase();
      this.sucata += conta.total;
      this.ultimaConta = conta;
    }

    if (venceu && temMais) {
      this.guardarProgresso(STAGE_BONUS_LIVES);
      this.abrirIntervalo();
      return;
    }

    this.guardarProgresso(0);
    this.state = 'ended';
    // guardado pra reenviar a quem voltar depois do fim
    this.ultimoFim = {
      t: 'end',
      result: w.result,
      stage: this.faseVisivel,
      totalFases: this.totalFases,
      modo: this.mode,
      campanhaCompleta: venceu && !temMais && this.mode === 'campanha',
      sucata: this.sucata,
      porTipo: this.porTipo(),
      scores: this.placar(),
    };
    this.broadcast(this.ultimoFim);
  }

  // ------------------------------------------------------------ intervalo

  abrirIntervalo() {
    this.state = 'intervalo';
    const proxima = CAMPAIGN[this.stage + 1];
    const mapa = MAPS[proxima.map] ?? MAPS[0];

    for (const p of this.players.values()) {
      p.pick = null;
      p.cartas = sortearCartas(p.upgrades);
      // guardado pra reenviar a quem voltar de uma queda no meio do intervalo
      p.ultimoInter = {
        t: 'inter',
        stage: this.stage,
        proxima: this.stage + 1,
        totalFases: this.totalFases,
        mapName: mapa.name,
        mapTag: mapa.tag,
        chefe: !!proxima.boss,
        segundos: Math.round(INTERMISSION_MS / 1000),
        cartas: p.cartas,
        placar: this.placar(),
        porTipo: this.porTipo(),
        conta: this.ultimaConta,
        sucata: this.sucata,
        moeda: MOEDA,
      };
      this.sendTo(p, p.ultimoInter);
    }
    this.sendEscolhas();

    this.faseTimer = setTimeout(() => { this.faseTimer = null; this.seguir(); }, INTERMISSION_MS);
  }

  escolherUpgrade(id, upId) {
    const p = this.players.get(id);
    if (!p || this.state !== 'intervalo' || p.pick) return;
    if (!p.cartas.some((c) => c.id === upId)) return;
    p.pick = upId;
    p.upgrades.push(upId);
    // Upgrade de evento (a vida extra) vale agora e só agora.
    if (UPGRADE_BY_ID[upId]?.umaVez) UPGRADE_BY_ID[upId].apply(p);
    this.sendEscolhas();
    this.talvezSeguir();
  }

  sendEscolhas() {
    this.broadcast({
      t: 'interup',
      escolhas: [...this.players.values()].map((p) => ({
        id: p.id,
        name: p.name,
        color: PLAYER_COLORS[p.slot],
        up: p.pick ? UPGRADE_BY_ID[p.pick]?.name : null,
        offline: p.offline,
      })),
    });
  }

  talvezSeguir() {
    if (this.state !== 'intervalo') return;
    if (this.online === 0) return;
    if ([...this.players.values()].some((p) => !p.offline && !p.pick)) return;
    this.stop();
    // Um respiro pra todo mundo ver a escolha dos outros antes da fase virar.
    this.faseTimer = setTimeout(() => { this.faseTimer = null; this.seguir(); }, 1400);
  }

  // Do intervalo o esquadrão não cai direto na fase: passa pela obra de novo.
  seguir() {
    if (this.state !== 'intervalo') return;
    if (this.empty) { this.state = 'lobby'; return; }
    this.stage += 1;
    this.abrirConstrucao();
  }

  // ------------------------------------------------------------ rede

  sendLobby() {
    this.broadcast({
      t: 'room',
      code: this.code,
      host: this.hostId,
      state: this.state,
      modo: this.mode,
      stage: this.stage,
      sucata: this.sucata,
      campanha: CAMPAIGN.map((f, i) => ({
        i, name: MAPS[f.map]?.name ?? '?', tag: MAPS[f.map]?.tag ?? '', chefe: !!f.boss,
      })),
      maps: MAPS.map((m) => ({ id: m.id, name: m.name, tag: m.tag })),
      players: [...this.players.values()].map((p) => ({
        id: p.id, name: p.name, cls: p.cls, skin: p.skin, slot: p.slot,
        color: PLAYER_COLORS[p.slot], offline: p.offline,
      })),
    });
  }

  sendSnapshot() {
    const w = this.world;
    const chefe = w.bossId ? w.tanks.find((t) => t.id === w.bossId) : null;
    const msg = {
      t: 'snap',
      k: w.tick,
      // Inimigo morto não vai no retrato: o cliente já viu a explosão e o
      // esquadrão ainda aparece morto porque o HUD lista as vidas de cada um.
      tanks: w.tanks.filter((t) => t.kind === 'player' || t.alive).map((t) => {
        const o = {
          i: t.id, x: Math.round(t.x * 4) / 4, y: Math.round(t.y * 4) / 4,
          d: t.dir, m: t.moving ? 1 : 0, a: t.alive ? 1 : 0,
          h: t.hp, hm: t.hpMax, s: t.shield > 0 ? 1 : 0,
          c: t.color, n: t.name, e: t.etype || t.cls, p: t.kind === 'player' ? 1 : 0,
          l: t.lives, sc: t.score, kl: t.kills,
        };
        if (t.boss) o.bs = 1;
        if (t.portador) o.pt = 1;
        if (t.dashT > 0) o.ab = t.dashT;
        if (t.pierce > 0) o.pi = 1;
        if (t.level > 0) o.lv = t.level;
        // `sp` é a velocidade já com os upgrades: sem ela o cliente preveria
        // com a velocidade de fábrica da classe e o tanque ficaria borrachudo
        // da fase 2 em diante, quando o Motor entra.
        if (t.kind === 'player') { o.ac = t.abilityCd; o.am = t.abilityCdMax; o.sk = t.skin; o.sp = t.speed; }
        return o;
      }),
      b: w.bullets.map((b) => ({ i: b.id, x: Math.round(b.x), y: Math.round(b.y), d: b.dir, p: b.power, pi: b.pierce ? 1 : 0 })),
      bn: w.bonus.length ? w.bonus.map((b) => ({ i: b.id, k: b.kind, x: b.x, y: b.y, l: b.life })) : undefined,
      mn: w.minas.length ? w.minas : undefined,
      tr: w.torres.some((s) => s.hp > 0)
        ? w.torres.filter((s) => s.hp > 0).map((s) => ({ i: s.id, x: s.x, y: s.y, h: s.hp, d: s.dir }))
        : undefined,
      dt: this.pendingDirty.length ? this.pendingDirty : undefined,
      fx: this.pendingFx.length ? this.pendingFx.map((f) => ({ ...f, x: Math.round(f.x), y: Math.round(f.y) })) : undefined,
      hud: {
        left: Math.max(0, w.enemiesLeft),
        queue: Math.max(0, w.toSpawn),
        base: w.baseAlive ? 1 : 0,
        baseHp: w.baseHp,
        baseHpMax: w.baseHpMax,
        stage: this.faseVisivel,
        total: this.totalFases,
        freeze: w.freeze,
        pa: w.shovel,
        sucata: this.sucata,
        boss: chefe && chefe.alive ? { hp: chefe.hp, max: chefe.hpMax } : null,
      },
    };
    this.broadcast(msg);
    this.pendingDirty = [];
    this.pendingFx = [];
  }

  sendTo(player, obj) {
    if (player.ws?.readyState === 1) player.ws.send(JSON.stringify(obj));
  }

  broadcast(obj) {
    const raw = JSON.stringify(obj);
    for (const p of this.players.values()) {
      if (p.ws?.readyState === 1) p.ws.send(raw);
    }
  }
}
