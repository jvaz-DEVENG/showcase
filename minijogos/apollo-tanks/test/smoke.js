// Teste de fumaça: roda a simulação sem rede e confere se as regras funcionam.
//   node test/smoke.js
import assert from 'node:assert/strict';
import { MAPS, parseMap } from '../shared/maps.js';
import {
  createWorld, createTank, stepWorld, moveTank, useAbility, setTile,
  soltarBonus, montarFortificacoes, muroDeAco,
} from '../shared/sim.js';
import { enemyTurn, spawnEnemy, spawnBoss, ENEMY_TYPES } from '../server/ai.js';
import { Room, custoDoProximo } from '../server/room.js';
import {
  TEAM, T, GRID, TILE, TANK, PLAYER_SPAWNS, ENEMY_SPAWNS, ENEMIES_PER_STAGE,
  PLAYER_COLORS, CAMPAIGN, UPGRADES, UPGRADE_BY_ID, BOSS, DASH_TICKS,
  STAGE_BONUS_LIVES, PA_TICKS, BONUS_IDS, SUCATA, MOEDA,
  PONTOS_INIMIGO, FORTIFICACOES, NIVEL_MAX, GRANADA_NO_CHEFE, MAX_PLAYERS,
} from '../shared/constants.js';

let falhas = 0;
function teste(nome, fn) {
  try { fn(); console.log(`  ok   ${nome}`); }
  catch (e) { falhas++; console.log(`  FALHA ${nome}\n       ${e.message}`); }
}

console.log('\nAPOLLO TANKS — teste de fumaça\n');

// ---------------------------------------------------------------- mapas

teste('os 5 mapas têm 26x26 e todos contêm base', () => {
  assert.equal(MAPS.length, 5, `esperado 5 mapas, tem ${MAPS.length}`);
  for (const m of MAPS) {
    const tiles = parseMap(m.rows);
    assert.equal(tiles.length, GRID * GRID, `${m.id}: tamanho errado`);
    const bases = [...tiles].filter((t) => t === T.BASE).length;
    assert.equal(bases, 4, `${m.id}: base deveria ocupar 4 células, tem ${bases}`);
    assert.ok(m.name && m.tag, `${m.id}: falta nome ou etiqueta`);
  }
});

teste('pontos de nascimento estão livres', () => {
  for (const m of MAPS) {
    const tiles = parseMap(m.rows);
    for (const s of [...PLAYER_SPAWNS, ...ENEMY_SPAWNS]) {
      for (let dy = 0; dy < 2; dy++) for (let dx = 0; dx < 2; dx++) {
        const i = (s.y / TILE + dy) * GRID + (s.x / TILE + dx);
        assert.equal(tiles[i], T.EMPTY, `${m.id}: nascimento em (${s.x},${s.y}) bloqueado`);
      }
    }
  }
});

// O tanque ocupa 2 linhas: um mapa com as faixas livres desencontradas trava
// o jogador num canto. Aqui a gente anda o campo inteiro de célula em célula.
teste('dá pra atravessar todo mapa sem quebrar parede', () => {
  const solido = (t) => t === T.BRICK || t === T.STEEL || t === T.WATER || t === T.BASE;
  for (const m of MAPS) {
    const tiles = parseMap(m.rows);
    const cabe = (cx, cy) => {
      if (cx < 0 || cy < 0 || cx > GRID - 2 || cy > GRID - 2) return false;
      for (let dy = 0; dy < 2; dy++) for (let dx = 0; dx < 2; dx++) {
        if (solido(tiles[(cy + dy) * GRID + cx + dx])) return false;
      }
      return true;
    };
    const visto = new Set();
    const fila = [];
    for (const s of PLAYER_SPAWNS) {
      const cx = s.x / TILE, cy = s.y / TILE;
      visto.add(cy * GRID + cx);
      fila.push([cx, cy]);
    }
    while (fila.length) {
      const [cx, cy] = fila.pop();
      for (const [dx, dy] of [[0, -1], [1, 0], [0, 1], [-1, 0]]) {
        const nx = cx + dx, ny = cy + dy, k = ny * GRID + nx;
        if (visto.has(k) || !cabe(nx, ny)) continue;
        visto.add(k);
        fila.push([nx, ny]);
      }
    }
    for (const s of ENEMY_SPAWNS) {
      const k = (s.y / TILE) * GRID + s.x / TILE;
      assert.ok(visto.has(k), `${m.name}: não dá pra chegar do nascimento até (${s.x},${s.y}) sem tiro`);
    }
  }
});

// Nenhum tanque pode estar sobreposto a tijolo/aço/água.
function emLugarValido(w, t, ctx = '') {
  for (let r = Math.floor(t.y / TILE); r <= Math.floor((t.y + 15) / TILE); r++) {
    for (let c = Math.floor(t.x / TILE); c <= Math.floor((t.x + 15) / TILE); c++) {
      const v = w.tiles[r * GRID + c];
      assert.ok(v !== T.BRICK && v !== T.STEEL && v !== T.WATER,
        `${ctx} ${t.id} dentro de bloco sólido em (${c},${r})`);
    }
  }
}

function mundoDeTeste(mapIdx = 0, nJogadores = 2) {
  const w = createWorld(MAPS[mapIdx]);
  w.toSpawn = ENEMIES_PER_STAGE;
  w.enemiesLeft = ENEMIES_PER_STAGE;
  w.enemySpawnCd = 5;
  w.aliveMax = 4;
  w.spawnEvery = 150;
  for (let i = 0; i < nJogadores; i++) {
    const t = createTank({
      id: `p${i}`, name: `P${i}`, kind: 'player', team: TEAM.PLAYER,
      cls: ['assalto', 'pesado', 'sniper', 'suporte'][i], color: PLAYER_COLORS[i],
      x: PLAYER_SPAWNS[i].x, y: PLAYER_SPAWNS[i].y, dir: 0, lives: 3,
    });
    t.slot = i;
    w.tanks.push(t);
  }
  return w;
}

// ---------------------------------------------------------------- regras

teste('tanque anda no corredor e para na parede', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  // Corredor livre na linha 9; a linha 10 tem tijolo nas colunas 10..15.
  t.x = 2 * TILE; t.y = 9 * TILE;
  for (let i = 0; i < 600; i++) moveTank(t, { dir: 1, move: true }, w.tiles, w.tanks);
  assert.ok(t.x > 2 * TILE, 'não andou nada');
  assert.ok(t.x + 16 <= 10 * TILE, `atravessou a parede (parou em x=${t.x})`);
  emLugarValido(w, t);
});

teste('tanque não sai do campo', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  for (const dir of [0, 3, 2, 1]) {
    for (let i = 0; i < 600; i++) moveTank(t, { dir, move: true }, w.tiles, w.tanks);
    assert.ok(t.x >= 0 && t.x <= 208 - 16 && t.y >= 0 && t.y <= 208 - 16, `saiu do campo indo pra ${dir}`);
    emLugarValido(w, t);
  }
});

teste('tiro quebra tijolo e aço só cede a tiro perfurante', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  const tijolos = () => [...w.tiles].filter((x) => x === T.BRICK).length;
  const acos = () => [...w.tiles].filter((x) => x === T.STEEL).length;

  t.x = 12 * TILE; t.y = 16 * TILE; t.dir = 0; t.power = 1;
  const b0 = tijolos(), a0 = acos();
  for (let i = 0; i < 200; i++) stepWorld(w, { p0: { dir: 0, move: false, fire: true } });
  assert.ok(tijolos() < b0, 'nenhum tijolo quebrou');
  assert.equal(acos(), a0, 'tiro comum destruiu aço');

  t.power = 2;
  for (let i = 0; i < 400; i++) stepWorld(w, { p0: { dir: 0, move: false, fire: true } });
  assert.ok(acos() < a0, 'tiro perfurante não destruiu aço');
});

teste('base destruída encerra a partida', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  // Coloca o tanque logo acima da base, olhando pra baixo, e abre caminho.
  t.x = 12 * TILE; t.y = 20 * TILE; t.dir = 2; t.power = 2;
  for (let i = 0; i < 400 && !w.over; i++) stepWorld(w, { p0: { dir: 2, move: false, fire: true } });
  assert.equal(w.over, true, 'partida não acabou');
  assert.equal(w.result, 'derrota');
  assert.equal(w.baseAlive, false);
});

teste('inimigos nascem e saem do ponto de nascimento', () => {
  const w = mundoDeTeste(0, 2);
  let spawns = 0;
  for (let i = 0; i < 1800; i++) {
    stepWorld(w, {}, {
      onEnemyTurn: (ww, ins) => {
        const vivos = ww.tanks.filter((x) => x.kind === 'enemy' && x.alive).length;
        if (ww.toSpawn > 0 && vivos < 4 && --ww.enemySpawnCd <= 0) {
          if (spawnEnemy(ww)) { ww.toSpawn--; ww.enemySpawnCd = 150; spawns++; }
          else ww.enemySpawnCd = 20;
        }
        enemyTurn(ww, ins);
      },
      onRespawn: (ww, tk) => {
        tk.x = PLAYER_SPAWNS[tk.slot].x; tk.y = PLAYER_SPAWNS[tk.slot].y;
        tk.alive = true; tk.hp = tk.hpMax; tk.shield = 180;
      },
    });
  }
  assert.ok(spawns >= 3, `só nasceram ${spawns} inimigos`);
  const inimigos = w.tanks.filter((t) => t.kind === 'enemy');
  // A maioria tem que ter saído do ponto onde nasceu — `some` passaria com um
  // andando e todos os outros parados.
  const nosPontos = new Set(ENEMY_SPAWNS.map((s) => `${s.x},${s.y}`));
  const sairam = inimigos.filter((e) => !nosPontos.has(`${e.x},${e.y}`)).length;
  assert.ok(sairam >= inimigos.length - 1,
    `${sairam} de ${inimigos.length} inimigos saíram do ponto de nascimento`);
});

teste('matar todos os inimigos dá vitória', () => {
  const w = mundoDeTeste(0, 1);
  w.toSpawn = 0;
  w.enemiesLeft = 1;
  const alvo = spawnEnemy(w);
  assert.ok(alvo, 'não nasceu inimigo');
  alvo.shield = 0;
  alvo.hp = 1;
  const bala = { id: 999, owner: 'p0', team: TEAM.PLAYER, x: alvo.x + 6, y: alvo.y + 6, dir: 2, speed: 2, power: 1, dead: false };
  w.bullets.push(bala);
  for (let i = 0; i < 10 && !w.over; i++) stepWorld(w, {});
  assert.equal(w.enemiesLeft, 0, 'contador de inimigos não baixou');
  assert.equal(w.over, true, 'partida não terminou');
  assert.equal(w.result, 'vitoria');
});

// ---------------------------------------------------------------- habilidades

teste('Arranque leva o tanque bem mais longe', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  t.x = 2 * TILE; t.y = 9 * TILE; t.dir = 1;

  const normal = { ...t, x: 2 * TILE };
  for (let i = 0; i < DASH_TICKS; i++) moveTank(normal, { dir: 1, move: true }, w.tiles, w.tanks);
  const semDash = normal.x;

  assert.equal(useAbility(w, t), true, 'a habilidade não saiu');
  assert.ok(t.abilityCd > 0, 'não entrou em recarga');
  for (let i = 0; i < DASH_TICKS; i++) {
    stepWorld(w, { p0: { dir: 1, move: true, fire: false } });
  }
  assert.ok(t.x > semDash + 10, `o Arranque não adiantou nada (${semDash} → ${t.x})`);
  assert.equal(useAbility(w, t), false, 'deu pra usar de novo antes da recarga');
});

teste('Bastião segura o tiro que mataria', () => {
  const w = mundoDeTeste(0, 2);
  const t = w.tanks[1];              // pesado
  t.shield = 0;
  t.x = 4 * TILE; t.y = 12 * TILE;
  assert.equal(useAbility(w, t), true);
  assert.ok(t.shield > 100, `escudo curto demais (${t.shield})`);

  const hpAntes = t.hp;
  w.bullets.push({
    id: 900, owner: 'inimigo', team: TEAM.ENEMY,
    x: t.x + 6, y: t.y + 6, dir: 2, speed: 2, power: 2, dead: false,
  });
  stepWorld(w, {});
  assert.equal(t.hp, hpAntes, 'o escudo deixou passar');
});

teste('Perfurar atravessa aço e segue caminho', () => {
  const w = mundoDeTeste(0, 3);
  const t = w.tanks[2];              // sniper
  // Fila de aço logo acima do tanque (linhas 7 e 8, colunas 10..15 no mapa 1).
  t.x = 12 * TILE; t.y = 10 * TILE; t.dir = 0; t.fireCd = 0;

  const acosAntes = [...w.tiles].filter((x) => x === T.STEEL).length;
  assert.equal(useAbility(w, t), true);
  assert.equal(t.pierce, 1, 'não carregou o tiro');

  stepWorld(w, { p2: { dir: 0, move: false, fire: true } });
  const bala = w.bullets.find((b) => b.owner === t.id);
  assert.ok(bala && bala.pierce, 'a bala saiu sem ser perfurante');

  for (let i = 0; i < 40; i++) stepWorld(w, { p2: { dir: 0, move: false, fire: false } });
  const acosDepois = [...w.tiles].filter((x) => x === T.STEEL).length;
  assert.ok(acosDepois < acosAntes, 'o aço nem foi arranhado');
  // Uma bala comum morreria no primeiro bloco; a perfurante abre a coluna toda.
  assert.ok(acosAntes - acosDepois >= 3, `abriu pouco: ${acosAntes - acosDepois} células`);
});

teste('Reparo levanta o muro da águia de uma vez', () => {
  const w = mundoDeTeste(0, 4);
  const t = w.tanks[3];              // suporte
  t.x = 8 * TILE; t.y = 20 * TILE;

  for (const i of w.baseWalls) setTile(w, i, T.EMPTY);
  const vazias = w.baseWalls.filter((i) => w.tiles[i] === T.EMPTY).length;
  assert.ok(vazias > 4, 'o teste não conseguiu derrubar o muro');

  assert.equal(useAbility(w, t), true);
  const aindaVazias = w.baseWalls.filter((i) => w.tiles[i] === T.EMPTY).length;
  assert.equal(aindaVazias, 0, `sobraram ${aindaVazias} buracos no muro`);
  assert.ok(t.shield > 0, 'quem usou não ganhou o escudo');
});

// ---------------------------------------------------------------- progressão

teste('a campanha tem 5 fases, cada uma mais cheia, e chefe só na última', () => {
  assert.equal(CAMPAIGN.length, 5);
  const comChefe = CAMPAIGN.filter((f) => f.boss);
  assert.equal(comChefe.length, 1, 'chefe deveria aparecer em exatamente uma fase');
  assert.equal(CAMPAIGN[CAMPAIGN.length - 1].boss, true, 'o chefe não está na última fase');
  for (const f of CAMPAIGN) {
    assert.ok(MAPS[f.map], `fase aponta pra mapa inexistente (${f.map})`);
    assert.ok(f.enemies > 0 && f.alive > 0);
  }
  const semChefe = CAMPAIGN.filter((f) => !f.boss).map((f) => f.enemies);
  for (let i = 1; i < semChefe.length; i++) {
    assert.ok(semChefe[i] > semChefe[i - 1], `fase ${i + 1} não é mais cheia que a anterior`);
  }
});

teste('upgrades mexem mesmo no tanque', () => {
  const base = createTank({ id: 'x', kind: 'player', team: TEAM.PLAYER, cls: 'assalto', x: 0, y: 0, lives: 3 });
  const turbo = createTank({
    id: 'y', kind: 'player', team: TEAM.PLAYER, cls: 'assalto', x: 0, y: 0, lives: 3,
    upgrades: ['motor', 'pente', 'blindagem', 'reserva'],
  });
  assert.ok(turbo.speed > base.speed, 'motor não acelerou');
  assert.equal(turbo.maxBullets, base.maxBullets + 1, 'pente não somou bala');
  assert.equal(turbo.hpMax, base.hpMax + 1, 'blindagem não somou vida do casco');
  assert.equal(turbo.hp, turbo.hpMax, 'nasceu machucado');
  // "Tanque reserva" é evento, não atributo: vale na escolha e NÃO pode ser
  // reaplicado a cada nascimento, senão renderia uma vida por fase.
  assert.equal(turbo.lives, base.lives, 'a vida extra foi reaplicada no nascimento');
  assert.equal(UPGRADE_BY_ID.reserva.umaVez, true, 'a vida extra deveria estar marcada como umaVez');
  assert.ok(UPGRADES.filter((u) => u.umaVez).length === 1, 'só a vida extra deveria ser de evento');

  // Todo upgrade da lista precisa aplicar sem estourar.
  for (const u of UPGRADES) {
    const t = createTank({ id: 'z', kind: 'player', team: TEAM.PLAYER, cls: 'sniper', x: 0, y: 0, lives: 3, upgrades: [u.id] });
    assert.ok(Number.isFinite(t.speed) && t.speed > 0, `${u.id} quebrou a velocidade`);
    assert.ok(Number.isFinite(t.fireCdMax) && t.fireCdMax > 0, `${u.id} quebrou a recarga`);
    assert.ok(UPGRADE_BY_ID[u.id] === u, `${u.id} não está no índice`);
  }
});

teste('o Colosso aguenta muito e solta barragem pros 4 lados', () => {
  const w = mundoDeTeste(4, 1);
  w.toSpawn = 0;
  const chefe = spawnBoss(w);
  assert.ok(chefe && chefe.boss, 'o chefe não nasceu');
  assert.equal(w.bossId, chefe.id, 'a sala não guardou quem é o chefe');
  // Aguenta muito mais que qualquer inimigo comum — é o que faz dele chefe.
  const maisDuro = Math.max(...ENEMY_TYPES.map((t) => t.hp));
  assert.ok(chefe.hp >= maisDuro * 4,
    `chefe com ${chefe.hp} de vida contra ${maisDuro} do inimigo mais duro`);

  // A barragem sai sozinha: basta rodar a IA até a hora dela. Contamos os
  // tiros pelo efeito, não pelas balas vivas — o chefe nasce colado na borda
  // e a bala que sai pra cima morre na parede no mesmo tick.
  let maiorRajada = 0;
  let teveBarragem = false;
  for (let i = 0; i < BOSS.barrageEvery + 30; i++) {
    stepWorld(w, {}, { onEnemyTurn: (ww, ins) => enemyTurn(ww, ins) });
    maiorRajada = Math.max(maiorRajada, w.fx.filter((f) => f.k === 'shot').length);
    if (w.fx.some((f) => f.k === 'barragem')) teveBarragem = true;
  }
  assert.ok(teveBarragem, 'a barragem não saiu');
  assert.ok(maiorRajada >= 4, `a maior rajada teve ${maiorRajada} tiros, esperado 4`);
});

// ---------------------------------------------------------------- bônus do NES

teste('a estrela sobe o tanque e a morte devolve pro básico', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  t.shield = 0;
  const balaBase = t.bulletSpeed, pentesBase = t.maxBullets, forcaBase = t.power;

  for (let n = 1; n <= NIVEL_MAX; n++) {
    const b = soltarBonus(w, 0, 0, 'estrela');
    b.x = t.x; b.y = t.y;
    stepWorld(w, {});
    assert.equal(t.level, n, `não subiu pro nível ${n}`);
  }
  assert.ok(t.bulletSpeed > balaBase, 'a estrela não acelerou o tiro');
  assert.equal(t.maxBullets, pentesBase + 1, 'a estrela não somou bala na tela');
  assert.ok(t.power >= 2 && t.power > forcaBase - 1, 'no nível 3 o tiro tem que rasgar aço');

  // um extra não passa do teto
  const b = soltarBonus(w, 0, 0, 'estrela');
  b.x = t.x; b.y = t.y;
  stepWorld(w, {});
  assert.equal(t.level, NIVEL_MAX, 'passou do nível máximo');

  // morrer devolve tudo pro de fábrica, como no original
  w.bullets.push({ id: 800, owner: 'x', team: TEAM.ENEMY, x: t.x + 6, y: t.y + 6, dir: 2, speed: 2, power: 1, dead: false });
  stepWorld(w, {});
  assert.equal(t.alive, false, 'o tanque não morreu');
  assert.equal(t.level, 0, 'a estrela sobreviveu à morte');
  assert.equal(t.bulletSpeed, balaBase, 'o tiro continuou turbinado depois de morrer');
  assert.equal(t.maxBullets, pentesBase, 'o pente continuou grande depois de morrer');
});

teste('o Relógio congela os inimigos', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  const inimigo = spawnEnemy(w, 0, false);
  inimigo.shield = 0;
  const antes = { x: inimigo.x, y: inimigo.y };

  const b = soltarBonus(w, 0, 0, 'relogio');
  b.x = t.x; b.y = t.y;
  stepWorld(w, {});
  assert.ok(w.freeze > 0, 'o congelamento não começou');

  for (let i = 0; i < 120; i++) {
    stepWorld(w, {}, { onEnemyTurn: (ww, ins) => enemyTurn(ww, ins) });
  }
  assert.equal(inimigo.x, antes.x, 'o inimigo andou congelado (x)');
  assert.equal(inimigo.y, antes.y, 'o inimigo andou congelado (y)');
  assert.equal(w.bullets.filter((x) => x.team === TEAM.ENEMY).length, 0, 'inimigo congelado atirou');
});

teste('a Pá blinda o muro da águia e depois devolve o tijolo', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  const b = soltarBonus(w, 0, 0, 'pa');
  b.x = t.x; b.y = t.y;
  stepWorld(w, {});

  assert.ok(w.shovel > 0, 'a Pá não ligou');
  assert.ok(w.baseWalls.every((i) => w.tiles[i] === T.STEEL), 'nem todo o muro virou aço');

  for (let i = 0; i < PA_TICKS + 5; i++) stepWorld(w, {});
  assert.equal(w.shovel, 0, 'a Pá não acabou');
  assert.ok(w.baseWalls.every((i) => w.tiles[i] === T.BRICK), 'o muro não voltou a ser tijolo');
});

teste('a Granada limpa o campo e credita quem pegou', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  w.toSpawn = 0;
  w.enemiesLeft = 3;
  for (let i = 0; i < 3; i++) { const e = spawnEnemy(w, 0, false); if (e) e.shield = 0; }
  const vivos = w.tanks.filter((x) => x.kind === 'enemy' && x.alive).length;
  assert.ok(vivos > 0, 'não nasceu inimigo pro teste');

  const abatesAntes = t.kills;
  const b = soltarBonus(w, 0, 0, 'granada');
  b.x = t.x; b.y = t.y;
  stepWorld(w, {});
  assert.equal(w.tanks.filter((x) => x.kind === 'enemy' && x.alive).length, 0, 'sobrou inimigo vivo');
  assert.equal(t.kills, abatesAntes + vivos, 'os abates não foram creditados');
});

teste('Capacete e Tanque dão escudo e vida', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  t.shield = 0;
  const vidas = t.lives;

  let b = soltarBonus(w, 0, 0, 'capacete');
  b.x = t.x; b.y = t.y;
  stepWorld(w, {});
  assert.ok(t.shield > 300, `escudo curto demais (${t.shield})`);

  b = soltarBonus(w, 0, 0, 'tanque');
  b.x = t.x; b.y = t.y;
  stepWorld(w, {});
  assert.equal(t.lives, vidas + 1, 'não ganhou vida');
});

teste('o inimigo piscante larga bônus ao cair', () => {
  const w = mundoDeTeste(0, 1);
  w.toSpawn = 0;
  const comum = spawnEnemy(w, 0, false);
  comum.shield = 0; comum.hp = 1;
  w.bullets.push({ id: 901, owner: 'p0', team: TEAM.PLAYER, x: comum.x + 6, y: comum.y + 6, dir: 2, speed: 2, power: 1, dead: false });
  stepWorld(w, {});
  assert.equal(w.bonus.length, 0, 'inimigo comum largou bônus');

  const portador = spawnEnemy(w, 0, true);
  assert.ok(portador?.portador, 'não nasceu portador');
  portador.shield = 0; portador.hp = 1;
  w.bullets.push({ id: 902, owner: 'p0', team: TEAM.PLAYER, x: portador.x + 6, y: portador.y + 6, dir: 2, speed: 2, power: 1, dead: false });
  stepWorld(w, {});
  assert.equal(w.bonus.length, 1, 'o portador não largou bônus');
  assert.ok(BONUS_IDS.includes(w.bonus[0].kind), 'bônus de tipo desconhecido');
});

teste('o bônus vale 500 pontos e some sozinho', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  const pontos = t.score;
  const b = soltarBonus(w, 0, 0, 'capacete');
  b.x = t.x; b.y = t.y;
  stepWorld(w, {});
  assert.equal(t.score, pontos + 500, 'não pagou os 500 pontos');

  const solto = soltarBonus(w, 0, 0, 'capacete');
  solto.x = 0; solto.y = 0;          // longe de todo mundo
  solto.life = 3;
  for (let i = 0; i < 5; i++) stepWorld(w, {});
  assert.equal(w.bonus.length, 0, 'o bônus não sumiu com o tempo');
});

// ---------------------------------------------------------------- gelo

teste('no gelo o tanque escorrega depois de soltar', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  // faixa de gelo no corredor livre da linha 9
  for (let cx = 1; cx < 12; cx++) for (let cy = 9; cy <= 10; cy++) w.tiles[cy * GRID + cx] = T.ICE;
  t.x = 1 * TILE; t.y = 9 * TILE; t.dir = 1;

  for (let i = 0; i < 20; i++) moveTank(t, { dir: 1, move: true }, w.tiles, w.tanks);
  const aoSoltar = t.x;
  for (let i = 0; i < 40; i++) moveTank(t, { dir: 1, move: false }, w.tiles, w.tanks);
  const parado = t.x;
  assert.ok(parado > aoSoltar + 4, `não escorregou (${aoSoltar} → ${parado})`);

  // e no chão seco não escorrega nada
  const seco = mundoDeTeste(0, 1).tanks[0];
  seco.x = 1 * TILE; seco.y = 9 * TILE; seco.dir = 1;
  for (let i = 0; i < 20; i++) moveTank(seco, { dir: 1, move: true }, w.tiles.map(() => T.EMPTY), []);
  const x0 = seco.x;
  for (let i = 0; i < 40; i++) moveTank(seco, { dir: 1, move: false }, w.tiles.map(() => T.EMPTY), []);
  assert.equal(seco.x, x0, 'escorregou fora do gelo');
});

// ---------------------------------------------------------------- pontuação

teste('cada inimigo vale o seu tanto e 20 mil pontos dão vida extra', () => {
  const w = mundoDeTeste(0, 1);
  const t = w.tanks[0];
  w.toSpawn = 0;
  const alvo = spawnEnemy(w, 0, false);
  alvo.shield = 0; alvo.hp = 1; alvo.etype = 'canhao';

  const pontosAntes = t.score;
  w.bullets.push({ id: 910, owner: 'p0', team: TEAM.PLAYER, x: alvo.x + 6, y: alvo.y + 6, dir: 2, speed: 2, power: 1, dead: false });
  stepWorld(w, {});
  assert.equal(t.score, pontosAntes + PONTOS_INIMIGO.canhao, 'o canhão não pagou 300');
  assert.equal(w.abatesPorTipo.canhao, 1, 'não contou o abate por tipo');

  const vidas = t.lives;
  t.score = t.proximaVida - 100;
  const outro = spawnEnemy(w, 0, false);
  outro.shield = 0; outro.hp = 1; outro.etype = 'basico';
  w.bullets.push({ id: 911, owner: 'p0', team: TEAM.PLAYER, x: outro.x + 6, y: outro.y + 6, dir: 2, speed: 2, power: 1, dead: false });
  stepWorld(w, {});
  assert.equal(t.lives, vidas + 1, 'não veio a vida extra dos 20 mil');
});

// ---------------------------------------------------------------- fortificações

teste('a muralha de aço cobre o muro conforme o nível', () => {
  // Números na mão de propósito: comparar com muroDeAco() seria comparar a
  // implementação com ela mesma, e qualquer fórmula passaria.
  const ESPERADO = { 1: 5, 2: 9, 3: 14 };
  for (const nivel of [1, 2, 3]) {
    const w = createWorld(MAPS[0]);
    assert.equal(w.baseWalls.length, 14, 'o muro da águia deveria ter 14 células');
    montarFortificacoes(w, { aco: nivel });
    const aco = w.baseWalls.filter((i) => w.tiles[i] === T.STEEL).length;
    assert.equal(aco, ESPERADO[nivel], `nível ${nivel}: ${aco} de aço, esperado ${ESPERADO[nivel]}`);
    if (nivel === 3) assert.equal(aco, w.baseWalls.length, 'no nível 3 o muro todo tem que ser aço');
    // e o aço tem que começar pelas células mais perto da águia
    const perto = muroDeAco(w.baseWalls, w.baseCells[0], nivel);
    assert.ok(perto.every((i) => w.tiles[i] === T.STEEL), 'blindou célula longe antes de célula perto');
  }
  const semNada = createWorld(MAPS[0]);
  montarFortificacoes(semNada, {});
  assert.equal(semNada.baseWalls.filter((i) => semNada.tiles[i] === T.STEEL).length, 0, 'virou aço sem comprar');
});

teste('a blindagem da águia come tiro antes de ela cair', () => {
  const w = mundoDeTeste(0, 1);
  montarFortificacoes(w, { blindagem: 2 });
  assert.equal(w.baseHpMax, 3, 'a blindagem não somou');

  const t = w.tanks[0];
  t.x = 12 * TILE; t.y = 20 * TILE; t.dir = 2; t.power = 2;
  let ticks = 0;
  while (!w.over && ticks < 900) { stepWorld(w, { p0: { dir: 2, move: false, fire: true } }); ticks++; }
  assert.equal(w.over, true, 'a águia nunca caiu');
  assert.equal(w.baseHp, 0, `sobrou blindagem (${w.baseHp})`);
});

teste('as minas explodem o inimigo que passa por cima', () => {
  const w = mundoDeTeste(0, 1);
  montarFortificacoes(w, { minas: 1 });
  assert.equal(w.minas.length, 2, `esperado 2 minas, tem ${w.minas.length}`);

  w.toSpawn = 0;
  w.enemiesLeft = 1;
  const alvo = spawnEnemy(w, 0, false);
  const cel = w.minas[0];
  alvo.x = (cel % GRID) * TILE;
  alvo.y = Math.floor(cel / GRID) * TILE;
  stepWorld(w, {});
  assert.equal(alvo.alive, false, 'a mina não explodiu o inimigo');
  assert.equal(w.minas.length, 1, 'a mina não foi gasta');
});

teste('a sentinela atira sozinha em quem entra na linha', () => {
  const w = mundoDeTeste(0, 1);
  montarFortificacoes(w, { sentinela: 1 });
  assert.equal(w.torres.length, 1, 'a torre não foi montada');
  const s = w.torres[0];

  w.toSpawn = 0;
  const alvo = spawnEnemy(w, 0, false);
  alvo.shield = 0;
  alvo.x = s.x;                       // logo acima da torre, mesma coluna
  alvo.y = Math.max(0, s.y - 40);

  let atirou = false;
  for (let i = 0; i < 120 && !atirou; i++) {
    stepWorld(w, {});
    atirou = w.bullets.some((b) => b.owner === s.id);
  }
  assert.ok(atirou, 'a sentinela nunca atirou');
});

teste('o auto-reparo repõe o muro sem precisar de Suporte', () => {
  const w = mundoDeTeste(0, 1);
  montarFortificacoes(w, { reparo: 2 });
  assert.equal(w.autoReparoCada, 240, 'o intervalo do reparo veio errado');

  for (const i of w.baseWalls) setTile(w, i, T.EMPTY);
  const buracos = w.baseWalls.filter((i) => w.tiles[i] === T.EMPTY).length;
  // ninguém de Suporte em campo: quem repõe é só a fortificação
  for (let i = 0; i < 300; i++) stepWorld(w, {});
  const depois = w.baseWalls.filter((i) => w.tiles[i] === T.EMPTY).length;
  assert.ok(depois < buracos, 'o auto-reparo não repôs nada');
});

teste('derrubar o Colosso vale 2000 pontos e ganha a fase', () => {
  const w = mundoDeTeste(4, 1);
  const t = w.tanks[0];
  w.toSpawn = 0;
  w.enemiesLeft = 1;
  const chefe = spawnBoss(w);
  chefe.shield = 0;

  const pontos = t.score;
  let tiros = 0;
  while (chefe.alive && tiros < 60) {
    w.bullets.push({
      id: 2000 + tiros, owner: 'p0', team: TEAM.PLAYER,
      x: chefe.x + 6, y: chefe.y + 6, dir: 2, speed: 2, power: 1, dead: false,
    });
    stepWorld(w, {});
    tiros++;
  }
  assert.equal(chefe.alive, false, `o chefe aguentou ${tiros} tiros e não caiu`);
  assert.ok(tiros >= 20, `o chefe caiu com ${tiros} tiros — frágil demais`);
  assert.equal(t.score, pontos + PONTOS_INIMIGO.colosso, 'o chefe não pagou 2000');
  assert.equal(w.abatesPorTipo.colosso, 1, 'o abate do chefe não foi contado por tipo');
  assert.equal(w.enemiesLeft, 0, 'o contador de inimigos não zerou');
  assert.equal(w.over, true, 'a fase não acabou com o chefe morto');
  assert.equal(w.result, 'vitoria');
});

teste('água barra o tanque mas deixa a bala passar; mato deixa tudo passar', () => {
  const w = mundoDeTeste(1, 1);          // Pântano tem água e mato
  const t = w.tanks[0];
  const cel = (cx, cy) => cy * GRID + cx;

  // faixa de água na linha 12-13, com o tanque à esquerda dela
  for (let cx = 6; cx <= 9; cx++) for (let cy = 12; cy <= 13; cy++) w.tiles[cel(cx, cy)] = T.WATER;
  for (let cx = 2; cx <= 5; cx++) for (let cy = 12; cy <= 13; cy++) w.tiles[cel(cx, cy)] = T.EMPTY;
  t.x = 2 * TILE; t.y = 12 * TILE; t.dir = 1;

  for (let i = 0; i < 400; i++) moveTank(t, { dir: 1, move: true }, w.tiles, w.tanks);
  assert.ok(t.x + TANK <= 6 * TILE, `o tanque entrou na água (parou em x=${t.x})`);

  // a bala atravessa a mesma faixa
  const bala = { id: 700, owner: 'p0', team: TEAM.PLAYER, x: 5 * TILE, y: 12 * TILE + 6, dir: 1, speed: 3, power: 1, dead: false };
  w.bullets.push(bala);
  for (let i = 0; i < 8 && w.bullets.length; i++) stepWorld(w, {});
  assert.ok(bala.x > 10 * TILE || bala.dead === false,
    `a bala parou na água em x=${bala.x}`);

  // mato: tanque e bala passam
  const w2 = mundoDeTeste(1, 1);
  const t2 = w2.tanks[0];
  for (let cx = 2; cx <= 12; cx++) for (let cy = 12; cy <= 13; cy++) w2.tiles[cel(cx, cy)] = T.TREES;
  t2.x = 2 * TILE; t2.y = 12 * TILE; t2.dir = 1;
  for (let i = 0; i < 400; i++) moveTank(t2, { dir: 1, move: true }, w2.tiles, w2.tanks);
  assert.ok(t2.x > 6 * TILE, `o mato barrou o tanque (parou em x=${t2.x})`);
});

// ---------------------------------------------------------------- sala

// Uma sala de mentirinha: os "jogadores" só guardam o que receberiam pela rede.
function salaDeTeste() {
  const room = new Room('TEST');
  const caixas = {};
  const conectar = (id, nome, cls) => {
    const caixa = [];
    caixas[id] = caixa;
    room.addPlayer(id, { readyState: 1, send: (raw) => caixa.push(JSON.parse(raw)) }, nome, cls);
  };
  conectar('c1', 'Ana', 'assalto');
  conectar('c2', 'Bia', 'suporte');
  return {
    room, caixas,
    ultima: (id, t) => [...caixas[id]].reverse().find((m) => m.t === t),
    // atalho: passa pelo Modo Construção sem esperar o relógio
    entrarNaFase: () => { room.prontoConstrucao('c1'); room.prontoConstrucao('c2'); },
  };
}

teste('o Modo Construção abre antes da fase e cobra a Sucata', () => {
  const { room, ultima } = salaDeTeste();
  room.begin();
  try {
    assert.equal(room.state, 'construcao', 'não abriu a obra antes da fase 1');
    const build = ultima('c1', 'build');
    assert.ok(build, 'ninguém recebeu a tela de construção');
    assert.equal(build.forts.length, FORTIFICACOES.length, 'faltou fortificação na lista');
    assert.equal(build.sucata, 0, 'começou com caixa cheia');
    assert.equal(build.moeda.nome, MOEDA.nome);

    // sem caixa, não compra
    const semGrana = room.comprarFort('c1', 'aco');
    assert.equal(semGrana.ok, false, 'comprou sem ter sucata');

    room.sucata = 1000;
    const custo = custoDoProximo(room.forts, 'aco');
    const r = room.comprarFort('c1', 'aco');
    assert.equal(r.ok, true, r.msg);
    assert.equal(room.forts.aco, 1, 'o nível não subiu');
    assert.equal(room.sucata, 1000 - custo, 'a sucata não foi debitada');

    // id inválido não passa
    assert.equal(room.comprarFort('c1', 'nao-existe').ok, false, 'aceitou fortificação inventada');

    // e o que foi comprado aparece no campo quando a fase começa
    room.prontoConstrucao('c1');
    assert.equal(room.state, 'construcao', 'começou com um jogador só pronto');
    room.prontoConstrucao('c2');
    assert.equal(room.state, 'playing', 'não entrou na fase com todo mundo pronto');
    assert.ok(room.world.baseWalls.some((i) => room.world.tiles[i] === T.STEEL),
      'a muralha comprada não foi montada');
  } finally {
    room.stop();
  }
});

teste('limpar a fase rende Sucata e a conta é discriminada', () => {
  const { room, ultima, entrarNaFase } = salaDeTeste();
  room.begin();
  try {
    entrarNaFase();
    room.stop();
    room.world.abatesPorTipo = { basico: 5 };
    room.world.over = true;
    room.world.result = 'vitoria';
    room.finish();

    const inter = ultima('c1', 'inter');
    assert.ok(inter, 'não veio o intervalo');
    const esperado = SUCATA.base + SUCATA.porAbate * 5 + SUCATA.aguiaIntacta + SUCATA.semMortes;
    assert.equal(inter.conta.total, esperado, `conta errada: ${inter.conta.total} em vez de ${esperado}`);
    assert.equal(inter.sucata, esperado, 'a caixa não recebeu o valor');
    assert.ok(inter.conta.linhas.length >= 3, 'a conta não veio discriminada');
    assert.equal(inter.porTipo[0].pontos, 5 * PONTOS_INIMIGO.basico, 'o placar por tipo veio errado');

    // com o muro quebrado e alguém morto, os dois bônus somem
    const outra = salaDeTeste();
    outra.room.begin();
    outra.entrarNaFase();
    outra.room.stop();
    outra.room.world.abatesPorTipo = { basico: 5 };
    outra.room.world.muroQuebrado = true;
    outra.room.world.tanks.find((t) => t.id === 'p0').lives = 1;
    outra.room.world.over = true;
    outra.room.world.result = 'vitoria';
    outra.room.finish();
    const inter2 = outra.ultima('c1', 'inter');
    assert.equal(inter2.conta.total, SUCATA.base + SUCATA.porAbate * 5, 'os bônus não foram descontados');
    outra.room.stop();
  } finally {
    room.stop();
  }
});

teste('o intervalo entre fases carrega vidas, pontos e upgrades', () => {
  const { room, ultima, entrarNaFase } = salaDeTeste();
  room.begin();
  try {
    entrarNaFase();
    assert.equal(room.state, 'playing');
    assert.equal(room.stage, 0, 'a campanha não começou na fase 1');

    // A fase 1 termina em vitória com a Ana machucada mas pontuada.
    const ana = room.world.tanks.find((t) => t.id === 'p0');
    ana.score = 500; ana.kills = 5; ana.lives = 2;
    room.world.over = true;
    room.world.result = 'vitoria';
    room.finish();

    assert.equal(room.state, 'intervalo', 'não abriu o intervalo depois da vitória');
    const c1 = ultima('c1', 'inter'), c2 = ultima('c2', 'inter');
    assert.ok(c1 && c2, 'nem todo mundo recebeu o intervalo');
    assert.equal(c1.cartas.length, 3, 'deveriam vir 3 cartas');
    assert.equal(c1.proxima, 1, 'a próxima fase deveria ser a 2');
    assert.ok(new Set(c1.cartas.map((x) => x.id)).size === 3, 'vieram cartas repetidas');

    // Carta inválida não passa; a da mão, sim.
    room.escolherUpgrade('c1', 'nao-existe');
    assert.equal(room.players.get('c1').upgrades.length, 0, 'aceitou upgrade que não estava na mão');
    room.escolherUpgrade('c1', c1.cartas[0].id);
    room.escolherUpgrade('c2', c2.cartas[1].id);
    assert.deepEqual(room.players.get('c1').upgrades, [c1.cartas[0].id]);
    room.escolherUpgrade('c1', c1.cartas[1].id);
    assert.equal(room.players.get('c1').upgrades.length, 1, 'deu pra escolher duas cartas na mesma rodada');

    // As vidas: 2 que sobraram + o bônus de fase. Se a carta sorteada foi a
    // vida extra, ela já foi paga aqui — por isso a conta sai do registro do
    // jogador e não de um número fixo, senão o teste falharia 1 em cada 8
    // execuções por causa do sorteio.
    const vidasEsperadas = 2 + STAGE_BONUS_LIVES + (room.players.get('c1').upgrades[0] === 'reserva' ? 1 : 0);
    assert.equal(room.players.get('c1').lives, vidasEsperadas,
      `a sala guardou ${room.players.get('c1').lives} vidas, esperado ${vidasEsperadas}`);

    // Pula a espera. Do intervalo o esquadrão passa pela obra de novo antes
    // de cair na fase 2 — é lá que a Sucata da fase 1 vira defesa.
    room.stop();
    room.seguir();
    assert.equal(room.state, 'construcao', 'o intervalo não levou pro Modo Construção');
    assert.equal(room.stage, 1, 'não avançou de fase');
    entrarNaFase();
    assert.equal(room.state, 'playing');
    assert.notEqual(room.world.mapId, MAPS[0].id, 'a fase 2 caiu no mesmo mapa da 1');

    const nova = room.world.tanks.find((t) => t.id === 'p0');
    assert.equal(nova.score, 500, 'os pontos não vieram junto');
    assert.equal(nova.lives, vidasEsperadas, `as vidas não vieram junto (${nova.lives})`);

    // O tanque da fase 2 tem que sair de fábrica com o upgrade escolhido.
    const referencia = createTank({
      id: 'ref', kind: 'player', team: TEAM.PLAYER, cls: 'assalto', x: 0, y: 0,
      lives: nova.lives, score: nova.score, upgrades: room.players.get('c1').upgrades,
    });
    for (const campo of ['speed', 'fireCdMax', 'maxBullets', 'bulletSpeed', 'power', 'hpMax', 'abilityCdMax']) {
      assert.equal(nova[campo], referencia[campo], `${campo} não veio com o upgrade aplicado`);
    }
  } finally {
    room.stop();
  }
});

teste('a vida extra é paga uma vez, não a cada fase', () => {
  const { room, entrarNaFase } = salaDeTeste();
  room.begin();
  try {
    entrarNaFase();
    // limpa a fase de verdade pra cair no intervalo, que é onde se escolhe
    room.stop();
    room.world.over = true;
    room.world.result = 'vitoria';
    room.finish();
    assert.equal(room.state, 'intervalo');
    room.stop();

    const p = room.players.get('c1');
    const vidas0 = p.lives;

    // força a carta de vida extra na mão, sem depender do sorteio
    p.pick = null;
    p.cartas = [{ id: 'reserva', name: 'Tanque reserva', icon: '♥', desc: '' }];
    room.escolherUpgrade('c1', 'reserva');
    room.stop();
    assert.equal(p.lives, vidas0 + 1, 'a vida não foi paga na escolha');

    // e agora ela NÃO pode somar de novo a cada fase que nasce
    for (let fase = 0; fase < 3; fase++) {
      room.stop();
      room.start(fase);
      const t = room.world.tanks.find((x) => x.id === p.tankId);
      assert.equal(t.lives, vidas0 + 1, `na fase ${fase + 1} o tanque nasceu com ${t.lives} vidas`);
      room.guardarProgresso(0);
      assert.equal(p.lives, vidas0 + 1, `depois da fase ${fase + 1} a sala guardou ${p.lives}`);
    }
  } finally {
    room.stop();
  }
});

teste('a granada e a mina machucam o chefe, mas não o matam', () => {
  const w = mundoDeTeste(4, 1);
  const t = w.tanks[0];
  w.toSpawn = 0;
  const chefe = spawnBoss(w);
  chefe.shield = 0;
  const cheio = chefe.hp;

  const b = soltarBonus(w, 0, 0, 'granada');
  b.x = t.x; b.y = t.y;
  stepWorld(w, {});
  assert.equal(chefe.alive, true, 'a granada matou o chefe de uma vez');
  assert.ok(chefe.hp < cheio, 'a granada não machucou o chefe');
  assert.equal(chefe.hp, cheio - GRANADA_NO_CHEFE, `tirou ${cheio - chefe.hp}, esperado ${GRANADA_NO_CHEFE}`);

  // já um inimigo comum some com a mesma granada
  const comum = spawnEnemy(w, 0, false);
  comum.shield = 0;
  const b2 = soltarBonus(w, 0, 0, 'granada');
  b2.x = t.x; b2.y = t.y;
  stepWorld(w, {});
  assert.equal(comum.alive, false, 'a granada não levou o inimigo comum');
  assert.equal(chefe.alive, true, 'a segunda granada matou o chefe');
});

teste('esquadrão sem vidas perde a partida', () => {
  const w = mundoDeTeste(0, 2);
  // o Pesado aguenta 2 tiros; aqui todo mundo cai com um só
  for (const t of w.tanks) { t.lives = 1; t.shield = 0; t.hp = 1; t.hpMax = 1; }
  assert.equal(w.over, false);

  // mata os dois: sem vida sobrando, a partida tem que acabar em derrota
  for (const t of w.tanks) {
    w.bullets.push({
      id: 1000 + t.slot, owner: 'inimigo', team: TEAM.ENEMY,
      x: t.x + 6, y: t.y + 6, dir: 2, speed: 2, power: 1, dead: false,
    });
  }
  for (let i = 0; i < 5 && !w.over; i++) stepWorld(w, {});
  assert.equal(w.over, true, 'a partida não acabou com todo mundo sem vida');
  assert.equal(w.result, 'derrota');
  assert.equal(w.baseAlive, true, 'a águia caiu junto sem motivo');
});

teste('a derrota não paga Sucata e encerra a campanha no meio', () => {
  const { room, ultima, entrarNaFase } = salaDeTeste();
  room.begin();
  try {
    entrarNaFase();
    room.stop();
    room.sucata = 250;
    room.world.abatesPorTipo = { basico: 4 };
    room.world.over = true;
    room.world.result = 'derrota';
    room.finish();

    assert.equal(room.state, 'ended', 'a derrota não encerrou a sala');
    assert.equal(room.sucata, 250, 'a derrota pagou sucata');
    const fim = ultima('c1', 'end');
    assert.ok(fim, 'ninguém recebeu o fim');
    assert.equal(fim.result, 'derrota');
    assert.equal(fim.campanhaCompleta, false, 'derrota marcada como campanha completa');
    assert.equal(fim.scores.length, 2, 'o placar não veio com os 2 jogadores');
    assert.ok(Array.isArray(fim.porTipo), 'o placar por tipo não veio na derrota');
    assert.ok(!ultima('c1', 'inter'), 'a derrota abriu intervalo');
  } finally {
    room.stop();
  }
});

teste('só dá pra iniciar do lobby — a obra não pode ser reiniciada', () => {
  const { room, entrarNaFase } = salaDeTeste();
  try {
    assert.equal(room.podeIniciar(), true, 'não dá pra iniciar do lobby');

    room.begin();
    assert.equal(room.state, 'construcao');
    // é aqui que doía: um segundo "iniciar" chamaria begin() e apagaria tudo
    assert.equal(room.podeIniciar(), false, 'aceita reiniciar durante a obra');
    room.sucata = 500;
    room.comprarFort('c1', 'minas');
    assert.equal(room.forts.minas, 1);
    const saldo = room.sucata;

    entrarNaFase();
    assert.equal(room.podeIniciar(), false, 'aceita reiniciar no meio da fase');
    assert.equal(room.forts.minas, 1, 'a fortificação sumiu ao entrar na fase');
    assert.equal(room.sucata, saldo, 'a sucata mudou sozinha ao entrar na fase');

    room.stop();
    room.world.over = true;
    room.world.result = 'vitoria';
    room.finish();
    assert.equal(room.state, 'intervalo');
    assert.equal(room.podeIniciar(), false, 'aceita reiniciar no intervalo');

    room.backToLobby();
    assert.equal(room.podeIniciar(), true, 'não dá pra recomeçar depois de voltar ao lobby');
  } finally {
    room.stop();
  }
});

teste('a sala recusa o quinto jogador sem quebrar', () => {
  const room = new Room('CHEIA');
  const ws = () => ({ readyState: 1, send: () => {} });
  try {
    for (let i = 0; i < MAX_PLAYERS; i++) {
      assert.equal(room.addPlayer(`c${i}`, ws(), `P${i}`, 'assalto').ok, true, `o ${i + 1}º devia entrar`);
    }
    assert.equal(room.podeEntrar(), false, 'a sala cheia ainda aceita gente');
    const r = room.addPlayer('c9', ws(), 'Tarde', 'assalto');
    assert.equal(r.ok, false, 'o quinto entrou');
    assert.ok(/cheia/i.test(r.msg), `mensagem estranha: ${r.msg}`);
    assert.equal(room.players.size, MAX_PLAYERS, 'a recusa mexeu na contagem');

    // e depois que a partida começa, ninguém mais entra
    room.begin();
    room.stop();
    assert.equal(room.podeEntrar(), false, 'aceita entrar com a partida em andamento');
  } finally {
    room.stop();
  }
});

teste('nome que não é texto não derruba a sala', () => {
  const room = new Room('TIPOS');
  const ws = () => ({ readyState: 1, send: () => {} });
  try {
    for (const [i, nome] of [12345, true, { a: 1 }, ['x'], null, undefined, ''].entries()) {
      const r = room.addPlayer(`c${i}`, ws(), nome, 'assalto');
      assert.equal(r.ok, true, `nome ${JSON.stringify(nome)} foi recusado`);
      assert.equal(typeof r.player.name, 'string', `nome ${JSON.stringify(nome)} não virou texto`);
      assert.ok(r.player.name.length > 0 && r.player.name.length <= 12,
        `nome ${JSON.stringify(nome)} virou "${r.player.name}"`);
      room.removePlayer(`c${i}`);
    }
  } finally {
    room.stop();
  }
});

teste('vencer a última fase encerra a campanha', () => {
  const { room, ultima } = salaDeTeste();
  room.mode = 'campanha';
  room.begin();
  try {
    room.stop();
    room.start(CAMPAIGN.length - 1);
    room.stop();
    room.world.over = true;
    room.world.result = 'vitoria';
    room.finish();

    assert.equal(room.state, 'ended', 'a campanha não terminou');
    const fim = ultima('c1', 'end');
    assert.ok(fim, 'ninguém recebeu o fim');
    assert.equal(fim.campanhaCompleta, true, 'não marcou a campanha como completa');
    assert.equal(fim.scores.length, 2, 'o placar não veio com os 2 jogadores');
  } finally {
    room.stop();
  }
});

teste('uma fase é ganha pelo caminho de verdade: nasce, morre, acaba', () => {
  const { room, ultima, entrarNaFase } = salaDeTeste();
  room.begin();
  try {
    entrarNaFase();
    room.stop();                      // o laço quem toca aqui é o teste

    // fase curta: 3 inimigos, 2 em campo, nascendo rápido
    const w = room.world;
    w.toSpawn = 3; w.enimigosLeft = undefined;
    w.enemiesLeft = 3; w.aliveMax = 2; w.spawnEvery = 20; w.enemySpawnCd = 1;

    let nasceram = 0, ticks = 0;
    const vistos = new Set();
    while (room.state === 'playing' && ticks < 60 * 60) {
      room.tick();
      for (const e of w.tanks) {
        if (e.kind !== 'enemy' || vistos.has(e.id)) continue;
        vistos.add(e.id);
        nasceram++;
      }
      // mata quem estiver em campo, sem escudo de nascimento
      for (const e of w.tanks) {
        if (e.kind !== 'enemy' || !e.alive || e.shield > 0) continue;
        w.bullets.push({
          id: 5000 + ticks, owner: 'p0', team: TEAM.PLAYER,
          x: e.x + 6, y: e.y + 6, dir: 2, speed: 2, power: 2, dead: false,
        });
      }
      ticks++;
    }

    assert.equal(nasceram, 3, `nasceram ${nasceram} inimigos, esperado 3`);
    assert.ok(ticks > 30, 'a fase acabou rápido demais pra ter rodado de verdade');
    assert.equal(w.result, 'vitoria', `a fase terminou em ${w.result}`);
    assert.equal(room.state, 'intervalo', 'a vitória não abriu o intervalo');
    const inter = ultima('c1', 'inter');
    assert.ok(inter?.conta?.total > 0, 'a fase vencida não pagou Sucata');
    assert.equal(inter.porTipo.reduce((s, x) => s + x.n, 0), 3, 'o placar não bate com os 3 abates');
  } finally {
    room.stop();
  }
});

teste('o jogador morto volta no lugar certo, com escudo', () => {
  const { room, entrarNaFase } = salaDeTeste();
  room.begin();
  try {
    entrarNaFase();
    room.stop();
    const w = room.world;
    const t = w.tanks.find((x) => x.id === 'p0');
    t.shield = 0;
    t.x = 4 * TILE; t.y = 10 * TILE;      // longe do ponto de nascimento
    const vidas = t.lives;

    w.bullets.push({
      id: 6000, owner: 'inimigo', team: TEAM.ENEMY,
      x: t.x + 6, y: t.y + 6, dir: 2, speed: 2, power: 2, dead: false,
    });
    room.tick();
    assert.equal(t.alive, false, 'o tanque não morreu');
    assert.equal(t.lives, vidas - 1, 'a vida não foi descontada');

    for (let i = 0; i < 200 && !t.alive; i++) room.tick();
    assert.equal(t.alive, true, 'o tanque nunca voltou');
    assert.equal(t.x, PLAYER_SPAWNS[0].x, 'voltou na coluna errada');
    assert.equal(t.y, PLAYER_SPAWNS[0].y, 'voltou na linha errada');
    assert.ok(t.shield > 0, 'voltou sem escudo, dá pra morrer no ato');
    assert.equal(t.hp, t.hpMax, 'voltou machucado');
    assert.equal(t.dashT, 0, 'voltou deslizando');
  } finally {
    room.stop();
  }
});

teste('quem cai tem a vaga guardada e volta com tudo', () => {
  const { room, entrarNaFase } = salaDeTeste();
  room.begin();
  try {
    entrarNaFase();
    room.stop();

    const p = room.players.get('c1');
    const token = p.token;
    p.upgrades.push('motor');
    p.score = 777;
    assert.ok(token, 'o jogador não recebeu cracha');

    room.removePlayer('c1');
    assert.equal(room.players.size, 2, 'a vaga foi apagada em vez de guardada');
    assert.equal([...room.players.values()].find((x) => x.token === token).offline, true,
      'quem caiu não ficou marcado como offline');
    assert.equal(room.online, 1, 'a contagem de online não bateu');
    assert.equal(room.hostId, 'c2', 'o host não passou pra quem ficou');

    // cracha errado não entra
    assert.equal(room.reconectar('outro-cracha', {}, 'c9'), null, 'cracha inválido entrou');

    const ws = { readyState: 1, send: () => {} };
    const voltou = room.reconectar(token, ws, 'c1-novo');
    assert.ok(voltou, 'o cracha certo não conseguiu voltar');
    assert.equal(voltou.offline, false, 'voltou ainda marcado como offline');
    assert.equal(voltou.score, 777, 'perdeu os pontos na volta');
    assert.deepEqual(voltou.upgrades, ['motor'], 'perdeu os upgrades na volta');
    assert.equal(room.players.has('c1-novo'), true, 'a vaga não trocou de identificador');
    assert.equal(room.online, 2, 'a contagem de online não voltou');
  } finally {
    room.stop();
  }
});

teste('sala sem ninguém online não roda a simulação sozinha', () => {
  const { room, entrarNaFase } = salaDeTeste();
  room.begin();
  try {
    entrarNaFase();
    room.stop();
    const antes = room.world.tick;
    room.removePlayer('c1');
    room.removePlayer('c2');
    assert.equal(room.online, 0, 'ainda tem gente online');

    room.lastPump = 0;                 // finge que passou muito tempo
    room.pump();
    assert.equal(room.world.tick, antes, 'a simulação andou com a sala vazia');
  } finally {
    room.stop();
  }
});

teste('a fase avulsa não abre intervalo', () => {
  const { room, ultima, entrarNaFase } = salaDeTeste();
  room.mode = 'treino';
  room.stage = 2;
  room.begin();
  try {
    entrarNaFase();
    room.stop();
    assert.equal(room.world.mapId, MAPS[2].id, 'o treino não abriu o mapa escolhido');
    room.world.over = true;
    room.world.result = 'vitoria';
    room.finish();
    assert.equal(room.state, 'ended', 'o treino abriu intervalo em vez de terminar');
    assert.equal(ultima('c1', 'end').campanhaCompleta, false);
  } finally {
    room.stop();
  }
});

// ---------------------------------------------------------------- carga

teste('até 4 minutos simulados nos 5 mapas, sem travar nem vazar', () => {
  for (let mapa = 0; mapa < MAPS.length; mapa++) {
    const w = mundoDeTeste(mapa, 4);
    let ticks = 0, maxBalas = 0;
    // Jogadores andam e usam habilidade, mas não atiram: assim o teste mede a
    // IA e a estabilidade sem o time destruir a própria águia (o que é
    // permitido, igual no original).
    while (!w.over && ticks < 60 * 240) {
      const ins = {};
      for (const t of w.tanks) {
        if (t.kind !== 'player') continue;
        ins[t.id] = {
          dir: (ticks + t.slot * 37) % 240 < 60 ? (t.slot + Math.floor(ticks / 60)) % 4 : t.dir,
          move: true,
          fire: false,
          ab: ticks % 200 === t.slot * 13,
        };
      }
      stepWorld(w, ins, {
        onEnemyTurn: (ww, i2) => {
          const vivos = ww.tanks.filter((x) => x.kind === 'enemy' && x.alive).length;
          if (ww.toSpawn > 0 && vivos < 4 && --ww.enemySpawnCd <= 0) {
            if (spawnEnemy(ww, mapa % 4)) { ww.toSpawn--; ww.enemySpawnCd = 150; } else ww.enemySpawnCd = 20;
          }
          enemyTurn(ww, i2);
        },
        onRespawn: (ww, tk) => {
          tk.x = PLAYER_SPAWNS[tk.slot].x; tk.y = PLAYER_SPAWNS[tk.slot].y;
          tk.alive = true; tk.hp = tk.hpMax; tk.shield = 180;
        },
      });
      maxBalas = Math.max(maxBalas, w.bullets.length);
      assert.ok(w.bullets.length < 60, `${MAPS[mapa].name}: balas vazando (${w.bullets.length})`);
      for (const t of w.tanks) if (t.alive) emLugarValido(w, t, MAPS[mapa].name);
      ticks++;
    }
    // Sem isto o teste passaria com a simulação travando no primeiro tick.
    // Não se cobra que a partida ACABE: os jogadores deste teste não atiram, e
    // se a IA não chegar na águia em 4 minutos a partida segue viva — legítimo,
    // e depender disso deixaria o teste refém da sorte do sorteio da IA.
    assert.ok(ticks > 60 * 5, `${MAPS[mapa].name}: rodou só ${ticks} ticks antes de acabar`);
    assert.ok(maxBalas > 0, `${MAPS[mapa].name}: ninguém atirou o tempo todo`);
    console.log(`       ${MAPS[mapa].name}: ${(ticks / 60).toFixed(0)}s · fim=${w.result ?? 'em andamento'} · pico de ${maxBalas} balas`);
  }
});

console.log(falhas ? `\n${falhas} falha(s)\n` : '\nTudo certo.\n');
process.exit(falhas ? 1 : 0);
