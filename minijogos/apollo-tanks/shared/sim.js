// Simulação do jogo. O servidor é a autoridade: roda stepWorld() 60x por segundo.
// O cliente reaproveita moveTank() para prever o próprio tanque enquanto o
// pacote do servidor não chega (client-side prediction).
//
// Só moveTank() precisa ser determinística — é a única coisa daqui que roda
// nos dois lados. O resto de stepWorld() só existe no servidor, e por isso
// pode sortear (queda de bônus, por exemplo).

import {
  TILE, GRID, FIELD, TANK, DIRV, T, TEAM, CLASSES, ABILITIES, UPGRADE_BY_ID,
  RESPAWN_TICKS, DASH_MULT, DASH_TICKS, GELO_DESLIZE, BONUS_IDS, BONUS_DURACAO,
  BONUS_PONTOS, CAPACETE_TICKS, RELOGIO_TICKS, PA_TICKS, NIVEL_MAX,
  PONTOS_INIMIGO, VIDA_EXTRA_A_CADA, SENTINELA, PLAYER_SPAWNS,
  GRANADA_NO_CHEFE, MINA_NO_CHEFE,
} from './constants.js';
import { parseMap, baseWallCells, findBaseCells } from './maps.js';

export const cellIndex = (cx, cy) => cy * GRID + cx;
const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);

export function tileAt(tiles, cx, cy) {
  if (cx < 0 || cy < 0 || cx >= GRID || cy >= GRID) return T.STEEL; // fora do campo = parede
  return tiles[cy * GRID + cx];
}

const blocksTank = (t) => t === T.BRICK || t === T.STEEL || t === T.WATER || t === T.BASE || t === T.BASE_DEAD;
const blocksBullet = (t) => t === T.BRICK || t === T.STEEL || t === T.BASE;

function rectHitsTiles(x, y, w, h, tiles, pred) {
  const c0 = Math.floor(x / TILE), c1 = Math.floor((x + w - 0.001) / TILE);
  const r0 = Math.floor(y / TILE), r1 = Math.floor((y + h - 0.001) / TILE);
  for (let cy = r0; cy <= r1; cy++)
    for (let cx = c0; cx <= c1; cx++)
      if (pred(tileAt(tiles, cx, cy))) return true;
  return false;
}

function overlapsTank(self, x, y, tanks) {
  for (const o of tanks) {
    if (o === self || !o.alive || o.id === self.id) continue;
    if (x < o.x + TANK && x + TANK > o.x && y < o.y + TANK && y + TANK > o.y) return true;
  }
  return false;
}

function tankBlocked(self, x, y, tiles, tanks) {
  if (x < 0 || y < 0 || x > FIELD - TANK || y > FIELD - TANK) return true;
  if (rectHitsTiles(x, y, TANK, TANK, tiles, blocksTank)) return true;
  return overlapsTank(self, x, y, tanks);
}

const sobreGelo = (tank, tiles) =>
  rectHitsTiles(tank.x, tank.y, TANK, TANK, tiles, (t) => t === T.ICE);

// ---------------------------------------------------------------- movimento

// Velocidade efetiva: o Arranque multiplica por um tempo curto.
export const tankSpeed = (tank) => tank.speed * (tank.dashT > 0 ? DASH_MULT : 1);

// Usada pelo servidor E pelo cliente (previsão). Precisa ser determinística.
export function moveTank(tank, input, tiles, tanks) {
  if (!tank.alive) return;

  // Troca de direção alinha o tanque na grade de 8u, igual ao Battle City original.
  if (input.dir != null && input.dir !== tank.dir) {
    tank.dir = input.dir;
    const axis = input.dir === 0 || input.dir === 2 ? 'x' : 'y';
    const before = tank[axis];
    tank[axis] = clamp(Math.round(before / TILE) * TILE, 0, FIELD - TANK);
    if (tankBlocked(tank, tank.x, tank.y, tiles, tanks)) tank[axis] = before;
  }

  // No gelo o tanque não para na hora: guarda um resto de embalo. Diferente do
  // NES, aqui o deslize segue a direção nova se você virar — trava menos.
  if (input.move) tank.slide = sobreGelo(tank, tiles) ? GELO_DESLIZE : 0;
  else if (tank.slide > 0) tank.slide--;

  const andando = !!input.move || tank.slide > 0;
  tank.moving = andando;
  if (!andando) return;

  const [dx, dy] = DIRV[tank.dir];
  const v = tankSpeed(tank);
  const nx = clamp(tank.x + dx * v, 0, FIELD - TANK);
  const ny = clamp(tank.y + dy * v, 0, FIELD - TANK);
  if (!tankBlocked(tank, nx, ny, tiles, tanks)) {
    tank.x = nx;
    tank.y = ny;
    tank.tread = (tank.tread + v) % 8;
  } else {
    tank.slide = 0;                    // bateu: acabou o embalo
  }
}

// ---------------------------------------------------------------- disparo

// Põe uma bala no mundo saindo do cano do tanque. Usada pelo tiro normal,
// pela barragem do chefe e pelas sentinelas.
export function fireBullet(world, tank, dir, opts = {}) {
  const [dx, dy] = DIRV[dir];
  const bullet = {
    id: world.nextId++,
    owner: opts.owner ?? tank.id,
    team: opts.team ?? tank.team,
    x: tank.x + TANK / 2 + dx * (TANK / 2) - 2,
    y: tank.y + TANK / 2 + dy * (TANK / 2) - 2,
    dir,
    speed: opts.speed ?? tank.bulletSpeed,
    power: opts.power ?? tank.power,
    pierce: !!opts.pierce,
    hits: null,
    dead: false,
  };
  if (bullet.pierce) bullet.hits = new Set();
  world.bullets.push(bullet);
  world.fx.push({ k: 'shot', x: bullet.x + 2, y: bullet.y + 2, d: dir });
  return bullet;
}

export function tryFire(world, tank) {
  if (!tank.alive || tank.fireCd > 0) return null;
  let live = 0;
  for (const b of world.bullets) if (b.owner === tank.id) live++;
  if (live >= tank.maxBullets) return null;

  tank.fireCd = tank.fireCdMax;

  // "Perfurar" gasta a carga no próximo tiro: ele atravessa tudo.
  if (tank.pierce > 0) {
    tank.pierce--;
    return fireBullet(world, tank, tank.dir, { pierce: true, power: 3, speed: Math.max(tank.bulletSpeed, 4.2) });
  }
  return fireBullet(world, tank, tank.dir);
}

// ---------------------------------------------------------------- habilidades

// Devolve true se a habilidade saiu. O servidor chama isso quando o jogador
// aperta a tecla; a recarga é conferida aqui e em lugar nenhum mais.
export function useAbility(world, tank) {
  if (!tank.alive || tank.abilityCd > 0) return false;
  const kind = tank.ability;
  if (!ABILITIES[kind]) return false;

  if (kind === 'arranque') {
    tank.dashT = DASH_TICKS;
  } else if (kind === 'bastiao') {
    tank.shield = Math.max(tank.shield, 200);
  } else if (kind === 'perfurar') {
    tank.pierce = 1;
    tank.fireCd = 0;
  } else if (kind === 'reparo') {
    repairBaseWall(world, tank);
  }

  tank.abilityCd = tank.abilityCdMax;
  world.fx.push({ k: 'hab', a: kind, x: tank.x + TANK / 2, y: tank.y + TANK / 2, c: tank.color });
  return true;
}

// Reparo: levanta de uma vez o muro inteiro da águia e dá escudo curto a quem
// estiver por perto. Não reconstrói em cima de ninguém.
function repairBaseWall(world, tank) {
  if (world.baseAlive) for (const i of world.baseWalls) reporTijolo(world, i);
  for (const t of world.tanks) {
    if (!t.alive || t.team !== tank.team) continue;
    if (Math.abs(t.x - tank.x) > 56 || Math.abs(t.y - tank.y) > 56) continue;
    t.shield = Math.max(t.shield, 120);
  }
}

// Repõe um tijolo do muro, respeitando quem está em cima e a Pá (que deixa o
// muro de aço enquanto dura).
function reporTijolo(world, i) {
  if (world.tiles[i] !== T.EMPTY) return false;
  const wx = (i % GRID) * TILE, wy = Math.floor(i / GRID) * TILE;
  const ocupado = world.tanks.some((t) => t.alive &&
    wx < t.x + TANK && wx + TILE > t.x && wy < t.y + TANK && wy + TILE > t.y);
  if (ocupado) return false;
  setTile(world, i, world.shovel > 0 || world.acoPermanente.has(i) ? T.STEEL : T.BRICK);
  return true;
}

// ---------------------------------------------------------------- mundo

export function createTank(opts) {
  const cls = CLASSES[opts.cls] || CLASSES.assalto;
  const ability = opts.ability ?? cls.ability;
  const tank = {
    id: opts.id,
    name: opts.name || '?',
    kind: opts.kind,                 // 'player' | 'enemy'
    team: opts.team,
    cls: opts.cls,
    color: opts.color,
    skin: opts.skin || 'liso',
    x: opts.x, y: opts.y,
    dir: opts.dir ?? (opts.team === TEAM.ENEMY ? 2 : 0),
    moving: false,
    tread: 0,
    slide: 0,
    alive: true,
    hp: opts.hp ?? cls.hp,
    hpMax: opts.hp ?? cls.hp,
    speed: opts.speed ?? cls.speed,
    fireCd: 0,
    fireCdMax: opts.fireCd ?? cls.fireCd,
    maxBullets: opts.maxBullets ?? cls.maxBullets,
    bulletSpeed: opts.bulletSpeed ?? cls.bulletSpeed,
    power: opts.power ?? cls.power,
    shield: opts.shield ?? 0,
    lives: opts.lives ?? 0,
    respawn: 0,
    kills: 0,
    score: opts.score ?? 0,
    boss: !!opts.boss,
    portador: !!opts.portador,       // inimigo piscando: solta bônus ao morrer
    // habilidade ativa
    ability,
    abilityCd: 0,
    abilityCdMax: ABILITIES[ability]?.cd ?? 600,
    dashT: 0,
    pierce: 0,
    // estrela do original: sobe o tanque, some quando você morre
    level: 0,
  };

  // Os upgrades ganhos na campanha são reaplicados toda vez que o tanque
  // nasce — menos os marcados `umaVez`, que já foram pagos na escolha.
  for (const id of opts.upgrades || []) {
    const u = UPGRADE_BY_ID[id];
    if (u && !u.umaVez) u.apply(tank);
  }
  tank.hp = tank.hpMax;

  // Guarda o "de fábrica" pra estrela poder somar em cima sem acumular erro.
  tank.base = {
    bulletSpeed: tank.bulletSpeed,
    maxBullets: tank.maxBullets,
    power: tank.power,
  };
  tank.proximaVida = VIDA_EXTRA_A_CADA * (Math.floor(tank.score / VIDA_EXTRA_A_CADA) + 1);
  return tank;
}

// Reaplica os efeitos da estrela por cima dos valores de fábrica.
export function aplicarNivel(tank) {
  const b = tank.base;
  tank.bulletSpeed = b.bulletSpeed * (tank.level >= 1 ? 1.35 : 1);
  tank.maxBullets = b.maxBullets + (tank.level >= 2 ? 1 : 0);
  tank.power = tank.level >= 3 ? Math.max(b.power, 2) : b.power;
}

export function createWorld(mapDef) {
  const tiles = parseMap(mapDef.rows);
  return {
    tick: 0,
    nextId: 1,
    mapId: mapDef.id,
    tiles,
    baseWalls: baseWallCells(tiles),
    baseCells: findBaseCells(tiles),
    tanks: [],
    bullets: [],
    bonus: [],            // bônus caídos no campo
    minas: [],            // índices de célula com mina
    torres: [],           // sentinelas compradas na construção
    fx: [],
    dirty: [],            // índices de células alteradas neste tick
    baseAlive: true,
    baseHp: 1,
    baseHpMax: 1,
    acoPermanente: new Set(),   // muro que a fortificação deixou de aço
    over: false,
    result: null,         // 'vitoria' | 'derrota'
    enemiesLeft: 0,
    toSpawn: 0,
    enemySpawnCd: 0,
    repairCd: 0,
    autoReparoCada: 0,
    autoReparoCd: 0,
    freeze: 0,            // relógio: inimigos parados
    shovel: 0,            // pá: muro em aço
    muroQuebrado: false,  // pro bônus de "águia intacta"
    abatesPorTipo: {},
    // campanha
    stage: 0,
    tier: 0,
    aliveMax: 4,
    spawnEvery: 150,
    wantBoss: false,
    bossId: null,
  };
}

export function setTile(world, i, type) {
  if (world.tiles[i] === type) return;
  world.tiles[i] = type;
  world.dirty.push(i, type);
}

// ---------------------------------------------------------------- fortificações

// Monta no campo o que o esquadrão comprou no Modo Construção. Roda uma vez,
// logo depois de criar o mundo.
// Quais células do muro a "Muralha de aço" cobre num certo nível: as mais
// próximas da águia primeiro. O cliente usa isso na prévia do Modo Construção.
export function muroDeAco(baseWalls, baseCell, nivel) {
  const n = Math.min(3, nivel | 0);
  if (n <= 0) return [];
  const bx = baseCell % GRID, by = Math.floor(baseCell / GRID);
  const dist = (i) => Math.abs((i % GRID) - bx) + Math.abs(Math.floor(i / GRID) - by);
  const ordem = [...baseWalls].sort((a, b) => dist(a) - dist(b));
  return ordem.slice(0, Math.round((ordem.length * n) / 3));
}

export function montarFortificacoes(world, forts = {}) {
  const base = world.baseCells[0] ?? 0;

  const nAco = forts.aco | 0;
  if (nAco > 0) {
    for (const i of muroDeAco(world.baseWalls, base, nAco)) {
      world.acoPermanente.add(i);
      if (world.tiles[i] === T.BRICK) setTile(world, i, T.STEEL);
    }
  }

  const nBlind = forts.blindagem | 0;
  world.baseHpMax = 1 + nBlind;
  world.baseHp = world.baseHpMax;

  const nReparo = forts.reparo | 0;
  world.autoReparoCada = [0, 480, 240][Math.min(2, nReparo)] || 0;
  world.autoReparoCd = world.autoReparoCada;

  const nMinas = forts.minas | 0;
  if (nMinas > 0) world.minas = escolherCelulas(world, nMinas * 2, 5);

  const nTorres = forts.sentinela | 0;
  if (nTorres > 0) {
    for (const i of escolherCelulas(world, nTorres, 4, true)) {
      world.torres.push({
        id: `s${world.nextId++}`,
        x: (i % GRID) * TILE, y: Math.floor(i / GRID) * TILE,
        hp: SENTINELA.hp, hpMax: SENTINELA.hp, cd: 30, dir: 0,
      });
    }
  }
  return world;
}

// Células livres em volta da águia, das mais próximas pras mais longes.
// `doisPorDois` exige espaço de tanque (a sentinela ocupa 16x16 na tela).
function escolherCelulas(world, quantas, raio, doisPorDois = false) {
  const base = world.baseCells[0] ?? 0;
  const bx = base % GRID, by = Math.floor(base / GRID);
  const proibidas = new Set();
  for (const s of PLAYER_SPAWNS) {
    for (let dy = -1; dy <= 2; dy++) for (let dx = -1; dx <= 2; dx++) {
      proibidas.add((s.y / TILE + dy) * GRID + (s.x / TILE + dx));
    }
  }
  const livre = (cx, cy) => {
    if (cx < 0 || cy < 0 || cx >= GRID || cy >= GRID) return false;
    const lados = doisPorDois ? [[0, 0], [1, 0], [0, 1], [1, 1]] : [[0, 0]];
    return lados.every(([ox, oy]) => {
      const i = (cy + oy) * GRID + (cx + ox);
      return cx + ox < GRID && cy + oy < GRID && world.tiles[i] === T.EMPTY && !proibidas.has(i);
    });
  };

  const cand = [];
  for (let dy = -raio; dy <= raio; dy++) {
    for (let dx = -raio; dx <= raio; dx++) {
      const cx = bx + dx, cy = by + dy;
      if (!livre(cx, cy)) continue;
      cand.push({ i: cy * GRID + cx, d: Math.abs(dx) + Math.abs(dy) });
    }
  }
  cand.sort((a, b) => a.d - b.d);

  const out = [];
  for (const c of cand) {
    if (out.length >= quantas) break;
    // não empilha duas coisas coladas
    if (out.some((i) => Math.abs((i % GRID) - (c.i % GRID)) < 2 && Math.abs(Math.floor(i / GRID) - Math.floor(c.i / GRID)) < 2)) continue;
    out.push(c.i);
  }
  return out;
}

// ---------------------------------------------------------------- dano

function killBase(world) {
  if (!world.baseAlive) return;
  world.baseAlive = false;
  for (const i of world.baseCells) setTile(world, i, T.BASE_DEAD);
  const c = world.baseCells[0] ?? 0;
  world.fx.push({ k: 'boom', big: true, x: (c % GRID) * TILE + 8, y: Math.floor(c / GRID) * TILE + 8 });
  world.over = true;
  world.result = 'derrota';
}

// A blindagem comprada na construção come tiro antes da águia cair.
function acertarBase(world) {
  if (!world.baseAlive) return;
  world.baseHp--;
  const c = world.baseCells[0] ?? 0;
  const x = (c % GRID) * TILE + 8, y = Math.floor(c / GRID) * TILE + 8;
  if (world.baseHp > 0) {
    world.fx.push({ k: 'aguia', x, y });
    world.fx.push({ k: 'spark', x, y });
    return;
  }
  killBase(world);
}

// Área danificada pelo tiro: fina no eixo do movimento, larga na perpendicular.
function damageTiles(world, b) {
  const [dx] = DIRV[b.dir];
  const horiz = dx !== 0;
  const cx = b.x + 2, cy = b.y + 2;
  const ax = horiz ? 2 : 5, ay = horiz ? 5 : 2;
  const c0 = Math.floor((cx - ax) / TILE), c1 = Math.floor((cx + ax - 0.001) / TILE);
  const r0 = Math.floor((cy - ay) / TILE), r1 = Math.floor((cy + ay - 0.001) / TILE);

  let hit = false;
  for (let gy = r0; gy <= r1; gy++) {
    for (let gx = c0; gx <= c1; gx++) {
      if (gx < 0 || gy < 0 || gx >= GRID || gy >= GRID) { hit = true; continue; }
      const i = gy * GRID + gx;
      const t = world.tiles[i];
      if (t === T.BRICK) {
        setTile(world, i, T.EMPTY); hit = true;
        if (world.baseWalls.includes(i)) world.muroQuebrado = true;
      } else if (t === T.STEEL) {
        hit = true;
        if (b.power >= 2) {
          setTile(world, i, T.EMPTY);
          if (world.baseWalls.includes(i)) world.muroQuebrado = true;
        }
      } else if (t === T.BASE) { hit = true; acertarBase(world); }
    }
  }
  return hit;
}

// Pontos e vida extra a cada 20 mil, como no original.
function pontuar(world, tank, quanto) {
  tank.score += quanto;
  if (tank.kind !== 'player') return;
  while (tank.score >= tank.proximaVida) {
    tank.lives++;
    tank.proximaVida += VIDA_EXTRA_A_CADA;
    world.fx.push({ k: 'vida', x: tank.x + 8, y: tank.y + 8 });
  }
}

// Morte de um inimigo, venha de bala, mina ou granada.
function matarInimigo(world, alvo, creditar) {
  alvo.alive = false;
  alvo.moving = false;
  alvo.dashT = 0;
  world.fx.push({ k: 'boom', big: !!alvo.boss, x: alvo.x + 8, y: alvo.y + 8 });
  world.enemiesLeft--;
  const tipo = alvo.etype || 'basico';
  world.abatesPorTipo[tipo] = (world.abatesPorTipo[tipo] || 0) + 1;

  if (creditar) {
    creditar.kills++;
    pontuar(world, creditar, PONTOS_INIMIGO[tipo] ?? 100);
  }
  if (alvo.portador) soltarBonus(world, alvo.x + 8, alvo.y + 8);
}

function hitTank(world, b, tank) {
  if (tank.shield > 0) return false;
  tank.hp -= b.power >= 2 && tank.hpMax > 1 ? 2 : 1;
  world.fx.push({ k: 'spark', x: tank.x + 8, y: tank.y + 8 });
  if (tank.hp > 0) return true;

  const killer = world.tanks.find((t) => t.id === b.owner);
  if (tank.kind === 'enemy') {
    matarInimigo(world, tank, killer);
    return true;
  }

  tank.alive = false;
  tank.moving = false;
  tank.dashT = 0;
  tank.level = 0;                    // a estrela some com a morte, como no NES
  aplicarNivel(tank);
  world.fx.push({ k: 'boom', big: false, x: tank.x + 8, y: tank.y + 8 });
  if (killer) { killer.kills++; pontuar(world, killer, 200); }
  tank.lives--;
  tank.respawn = tank.cls === 'suporte' ? Math.floor(RESPAWN_TICKS * 0.6) : RESPAWN_TICKS;
  return true;
}

// ---------------------------------------------------------------- bônus

// Larga um bônus numa célula livre do campo. Só o servidor roda isto.
export function soltarBonus(world, px, py, tipo = null) {
  const kind = tipo || BONUS_IDS[Math.floor(Math.random() * BONUS_IDS.length)];
  const livre = [];
  for (let cy = 1; cy < GRID - 3; cy++) {
    for (let cx = 1; cx < GRID - 2; cx++) {
      const t = world.tiles[cy * GRID + cx];
      if (t !== T.EMPTY && t !== T.ICE) continue;
      if (world.tiles[(cy + 1) * GRID + cx + 1] === T.BASE) continue;
      livre.push([cx, cy]);
    }
  }
  if (!livre.length) return null;
  const [cx, cy] = livre[Math.floor(Math.random() * livre.length)];
  const b = { id: world.nextId++, kind, x: cx * TILE, y: cy * TILE, life: BONUS_DURACAO };
  world.bonus.push(b);
  world.fx.push({ k: 'bonus', x: px, y: py });
  return b;
}

function pegarBonus(world, tank, b) {
  pontuar(world, tank, BONUS_PONTOS);
  world.fx.push({ k: 'pegou', x: b.x + 8, y: b.y + 8, a: b.kind });

  if (b.kind === 'capacete') {
    tank.shield = Math.max(tank.shield, CAPACETE_TICKS);
  } else if (b.kind === 'relogio') {
    world.freeze = RELOGIO_TICKS;
  } else if (b.kind === 'pa') {
    aplicarPa(world);
  } else if (b.kind === 'estrela') {
    tank.level = Math.min(NIVEL_MAX, tank.level + 1);
    aplicarNivel(tank);
  } else if (b.kind === 'granada') {
    for (const t of world.tanks) {
      if (t.kind !== 'enemy' || !t.alive) continue;
      // O chefe não some com uma granada: leva um baque grande e continua.
      // Senão a fase final cai com um bônus de 1 em 5 inimigos.
      if (t.boss) {
        t.hp -= GRANADA_NO_CHEFE;
        world.fx.push({ k: 'spark', x: t.x + 8, y: t.y + 8 });
        if (t.hp <= 0) matarInimigo(world, t, tank);
        continue;
      }
      matarInimigo(world, t, tank);
    }
    world.fx.push({ k: 'granada', x: FIELD / 2, y: FIELD / 2 });
  } else if (b.kind === 'tanque') {
    tank.lives++;
    world.fx.push({ k: 'vida', x: tank.x + 8, y: tank.y + 8 });
  }
}

function aplicarPa(world) {
  world.shovel = PA_TICKS;
  for (const i of world.baseWalls) {
    if (world.tiles[i] === T.BRICK || world.tiles[i] === T.EMPTY) setTile(world, i, T.STEEL);
  }
}

function fimDaPa(world) {
  for (const i of world.baseWalls) {
    if (world.acoPermanente.has(i)) continue;      // esse aço foi comprado, fica
    if (world.tiles[i] === T.STEEL) setTile(world, i, T.BRICK);
  }
}

function stepBonus(world) {
  if (world.freeze > 0) world.freeze--;
  if (world.shovel > 0 && --world.shovel === 0) fimDaPa(world);

  for (let i = world.bonus.length - 1; i >= 0; i--) {
    const b = world.bonus[i];
    if (--b.life <= 0) { world.bonus.splice(i, 1); continue; }
    const pego = world.tanks.find((t) => t.alive && t.kind === 'player' &&
      b.x < t.x + TANK && b.x + TANK > t.x && b.y < t.y + TANK && b.y + TANK > t.y);
    if (pego) {
      world.bonus.splice(i, 1);
      pegarBonus(world, pego, b);
    }
  }
}

// ---------------------------------------------------------------- minas e torres

function stepMinas(world) {
  if (!world.minas.length) return;
  for (let i = world.minas.length - 1; i >= 0; i--) {
    const cel = world.minas[i];
    const mx = (cel % GRID) * TILE, my = Math.floor(cel / GRID) * TILE;
    const vitima = world.tanks.find((t) => t.alive && t.kind === 'enemy' &&
      mx < t.x + TANK && mx + TILE > t.x && my < t.y + TANK && my + TILE > t.y);
    if (!vitima) continue;
    world.minas.splice(i, 1);
    world.fx.push({ k: 'boom', big: true, x: mx + 4, y: my + 4 });
    if (vitima.boss) { vitima.hp -= MINA_NO_CHEFE; if (vitima.hp <= 0) matarInimigo(world, vitima, null); }
    else matarInimigo(world, vitima, null);
  }
}

// A sentinela procura inimigo alinhado e sem parede no meio, e atira.
function stepTorres(world) {
  for (const s of world.torres) {
    if (s.hp <= 0) continue;
    if (s.cd > 0) { s.cd--; continue; }
    const sx = s.x + TANK / 2, sy = s.y + TANK / 2;
    for (const t of world.tanks) {
      if (!t.alive || t.kind !== 'enemy') continue;
      const tx = t.x + TANK / 2, ty = t.y + TANK / 2;
      let dir = null;
      if (Math.abs(ty - sy) < 10 && Math.abs(tx - sx) < SENTINELA.alcance) dir = tx > sx ? 1 : 3;
      else if (Math.abs(tx - sx) < 10 && Math.abs(ty - sy) < SENTINELA.alcance) dir = ty > sy ? 2 : 0;
      if (dir === null) continue;
      if (!linhaLivre(world, sx, sy, tx, ty)) continue;
      s.dir = dir;
      s.cd = SENTINELA.fireCd;
      fireBullet(world, s, dir, {
        owner: s.id, team: TEAM.PLAYER, speed: SENTINELA.bulletSpeed, power: 1,
      });
      break;
    }
  }
}

function linhaLivre(world, x0, y0, x1, y1) {
  const passos = Math.ceil(Math.hypot(x1 - x0, y1 - y0) / TILE);
  for (let i = 1; i < passos; i++) {
    const x = x0 + ((x1 - x0) * i) / passos;
    const y = y0 + ((y1 - y0) * i) / passos;
    if (blocksBullet(tileAt(world.tiles, Math.floor(x / TILE), Math.floor(y / TILE)))) return false;
  }
  return true;
}

// ---------------------------------------------------------------- balas

function stepBullets(world) {
  for (const b of world.bullets) {
    if (b.dead) continue;
    const [dx, dy] = DIRV[b.dir];
    let remaining = b.speed;
    // Avança em passinhos de 2u pra bala não atravessar parede fina.
    while (remaining > 0 && !b.dead) {
      const step = Math.min(2, remaining);
      remaining -= step;
      b.x += dx * step;
      b.y += dy * step;

      if (b.x < -4 || b.y < -4 || b.x > FIELD || b.y > FIELD) {
        b.dead = true;
        world.fx.push({ k: 'ping', x: clamp(b.x + 2, 0, FIELD), y: clamp(b.y + 2, 0, FIELD) });
        break;
      }
      if (rectHitsTiles(b.x, b.y, 4, 4, world.tiles, blocksBullet)) {
        damageTiles(world, b);
        // A bala perfurante abre caminho e segue; a comum morre onde bateu.
        if (!b.pierce) {
          b.dead = true;
          world.fx.push({ k: 'ping', x: b.x + 2, y: b.y + 2 });
          break;
        }
      }
      // sentinela na frente: bala inimiga derruba a torre
      if (b.team === TEAM.ENEMY) {
        const s = world.torres.find((s) => s.hp > 0 &&
          b.x < s.x + TANK && b.x + 4 > s.x && b.y < s.y + TANK && b.y + 4 > s.y);
        if (s) {
          s.hp--;
          world.fx.push({ k: s.hp > 0 ? 'spark' : 'boom', x: s.x + 8, y: s.y + 8 });
          b.dead = true;
          break;
        }
      }
      for (const tk of world.tanks) {
        if (!tk.alive || tk.id === b.owner) continue;
        if (tk.team === b.team) continue;   // fogo amigo desligado no modo co-op
        if (b.pierce && b.hits.has(tk.id)) continue;
        if (b.x < tk.x + TANK && b.x + 4 > tk.x && b.y < tk.y + TANK && b.y + 4 > tk.y) {
          hitTank(world, b, tk);
          if (b.pierce) { b.hits.add(tk.id); continue; }
          b.dead = true;
          break;
        }
      }
    }
  }

  // Balas de times opostos se anulam — menos a perfurante, que passa reto.
  for (let i = 0; i < world.bullets.length; i++) {
    const a = world.bullets[i];
    if (a.dead || a.pierce) continue;
    for (let j = i + 1; j < world.bullets.length; j++) {
      const c = world.bullets[j];
      if (c.dead || c.pierce || c.team === a.team) continue;
      if (a.x < c.x + 4 && a.x + 4 > c.x && a.y < c.y + 4 && a.y + 4 > c.y) {
        a.dead = c.dead = true;
        world.fx.push({ k: 'ping', x: a.x + 2, y: a.y + 2 });
        break;
      }
    }
  }

  world.bullets = world.bullets.filter((b) => !b.dead);
}

// Passiva do Suporte + auto-reparo comprado: repõem o muro da águia.
function stepRepair(world) {
  if (!world.baseAlive) return;

  if (world.autoReparoCada > 0 && --world.autoReparoCd <= 0) {
    world.autoReparoCd = world.autoReparoCada;
    for (const i of world.baseWalls) if (reporTijolo(world, i)) break;
  }

  if (--world.repairCd > 0) return;
  world.repairCd = 75;
  const medics = world.tanks.filter((t) => t.alive && t.cls === 'suporte' && t.kind === 'player');
  if (!medics.length) return;
  for (const i of world.baseWalls) {
    if (world.tiles[i] !== T.EMPTY) continue;
    const wx = (i % GRID) * TILE, wy = Math.floor(i / GRID) * TILE;
    if (!medics.some((m) => Math.abs(m.x + 8 - wx) < 40 && Math.abs(m.y + 8 - wy) < 40)) continue;
    if (reporTijolo(world, i)) return; // um tijolo por vez
  }
}

/**
 * Um passo de simulação.
 * @param inputs objeto id -> { dir, move, fire, ab }
 * @param hooks  { onEnemyTurn, onRespawn } — a IA vive no servidor, fora da sim pura
 */
export function stepWorld(world, inputs, hooks = {}) {
  world.dirty.length = 0;
  world.fx.length = 0;
  world.tick++;

  if (hooks.onEnemyTurn) hooks.onEnemyTurn(world, inputs);

  for (const tank of world.tanks) {
    if (tank.fireCd > 0) tank.fireCd--;
    if (tank.shield > 0) tank.shield--;
    if (tank.abilityCd > 0) tank.abilityCd--;
    if (tank.dashT > 0) {
      tank.dashT--;
      if (tank.alive && tank.moving && tank.dashT % 3 === 0) {
        world.fx.push({ k: 'rastro', x: tank.x + 8, y: tank.y + 8, c: tank.color });
      }
    }

    if (!tank.alive) {
      if (tank.kind === 'player' && tank.respawn > 0 && --tank.respawn === 0 && tank.lives > 0) {
        hooks.onRespawn?.(world, tank);
      }
      continue;
    }

    // O Relógio congela os inimigos: eles não andam nem atiram.
    if (world.freeze > 0 && tank.kind === 'enemy') { tank.moving = false; continue; }

    const input = inputs[tank.id];
    if (!input) { tank.moving = false; continue; }
    if (input.ab) useAbility(world, tank);
    moveTank(tank, input, world.tiles, world.tanks);
    if (input.fire) tryFire(world, tank);
  }

  stepTorres(world);
  stepBullets(world);
  stepMinas(world);
  stepBonus(world);
  stepRepair(world);

  if (!world.over) {
    const alivePlayers = world.tanks.filter((t) => t.kind === 'player' && (t.alive || t.lives > 0));
    if (world.tanks.some((t) => t.kind === 'player') && alivePlayers.length === 0) {
      world.over = true;
      world.result = 'derrota';
    } else if (world.enemiesLeft <= 0 && !world.tanks.some((t) => t.kind === 'enemy' && t.alive)) {
      world.over = true;
      world.result = 'vitoria';
    }
  }
  return world;
}

export { blocksTank, blocksBullet, rectHitsTiles, tankBlocked, clamp, sobreGelo };
