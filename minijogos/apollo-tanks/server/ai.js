// IA dos tanques inimigos. Fica no servidor de propósito: a simulação em
// shared/sim.js precisa ser determinística pra previsão do cliente funcionar,
// e a IA usa aleatoriedade.

import {
  DIRV, TANK, TEAM, ENEMY_SPAWNS, TILE, GRID, BOSS, CHANCE_PORTADOR,
} from '../shared/constants.js';
import { createTank, tankBlocked, fireBullet } from '../shared/sim.js';

const rnd = (n) => Math.floor(Math.random() * n);
const pick = (arr) => arr[rnd(arr.length)];

// Os 4 inimigos clássicos. As cores são de meio-tom e frias de propósito —
// é o contraste de luz contra os jogadores (claros) que faz amigo e inimigo
// se separarem mesmo pra quem não distingue matiz.
// `w` é o peso do sorteio por tier: quanto mais
// fundo na campanha, mais os pesados aparecem.
export const ENEMY_TYPES = [
  { id: 'basico', color: '#7d8694', speed: 0.55, fireCd: 55, maxBullets: 1, bulletSpeed: 2.2, power: 1, hp: 1, w: [46, 34, 22, 12] },
  { id: 'veloz', color: '#5a6ea8', speed: 1.05, fireCd: 55, maxBullets: 1, bulletSpeed: 2.2, power: 1, hp: 1, w: [26, 26, 24, 22] },
  { id: 'canhao', color: '#b05f24', speed: 0.60, fireCd: 30, maxBullets: 2, bulletSpeed: 3.6, power: 1, hp: 1, w: [20, 25, 30, 33] },
  { id: 'blindado', color: '#8f4356', speed: 0.50, fireCd: 50, maxBullets: 1, bulletSpeed: 2.4, power: 1, hp: 4, w: [8, 15, 24, 33] },
];

function rollType(tier = 0) {
  const t = Math.max(0, Math.min(3, tier | 0));
  const total = ENEMY_TYPES.reduce((s, x) => s + x.w[t], 0);
  let r = rnd(total);
  for (const x of ENEMY_TYPES) { r -= x.w[t]; if (r < 0) return x; }
  return ENEMY_TYPES[0];
}

function pontoLivre(world) {
  const free = ENEMY_SPAWNS.filter(
    (s) => !world.tanks.some((t) => t.alive && Math.abs(t.x - s.x) < TANK && Math.abs(t.y - s.y) < TANK)
  );
  return free.length ? pick(free) : null;
}

export function spawnEnemy(world, tier = 0, portador = null) {
  const spot = pontoLivre(world);
  if (!spot) return null;

  const type = rollType(tier);
  // Um em cada cinco vem piscando: é o que larga bônus ao morrer, como no NES.
  const trazBonus = portador === null ? Math.random() < CHANCE_PORTADOR : !!portador;
  // Fases mais fundas deixam todo mundo um tico mais afiado.
  const gume = 1 + Math.max(0, Math.min(3, tier)) * 0.06;
  const tank = createTank({
    id: `e${world.nextId++}`,
    name: type.id,
    kind: 'enemy',
    team: TEAM.ENEMY,
    cls: 'assalto',
    color: type.color,
    x: spot.x, y: spot.y,
    dir: 2,
    speed: type.speed * gume,
    fireCd: Math.max(14, Math.round(type.fireCd / gume)),
    maxBullets: type.maxBullets,
    bulletSpeed: type.bulletSpeed,
    power: type.power,
    hp: type.hp,
    shield: 45,
    portador: trazBonus,
  });
  tank.etype = type.id;
  tank.ai = { think: 0, fire: 30 + rnd(40), dir: 2 };
  world.tanks.push(tank);
  world.fx.push({ k: 'spawn', x: spot.x + 8, y: spot.y + 8 });
  return tank;
}

// O Colosso da última fase: aguenta muito, atira rápido e de vez em quando
// solta uma barragem pros 4 lados de uma vez.
export function spawnBoss(world) {
  const spot = pontoLivre(world) || ENEMY_SPAWNS[1];
  const tank = createTank({
    id: `boss${world.nextId++}`,
    name: BOSS.name,
    kind: 'enemy',
    team: TEAM.ENEMY,
    cls: 'pesado',
    color: BOSS.color,
    x: spot.x, y: spot.y,
    dir: 2,
    speed: BOSS.speed,
    fireCd: BOSS.fireCd,
    maxBullets: BOSS.maxBullets,
    bulletSpeed: BOSS.bulletSpeed,
    power: BOSS.power,
    hp: BOSS.hp,
    shield: 60,
    boss: true,
  });
  tank.etype = BOSS.id;
  tank.ai = { think: 0, fire: 40, dir: 2, barrage: BOSS.barrageEvery };
  world.tanks.push(tank);
  world.bossId = tank.id;
  world.fx.push({ k: 'chefe', x: spot.x + 8, y: spot.y + 8 });
  return tank;
}

function canGo(world, tank, dir) {
  const [dx, dy] = DIRV[dir];
  const step = Math.max(tank.speed, 1) * 4;
  return !tankBlocked(tank, tank.x + dx * step, tank.y + dy * step, world.tiles, world.tanks);
}

// Alvo: a base na maior parte do tempo, um jogador de vez em quando.
function chooseTarget(world, tank) {
  const players = world.tanks.filter((t) => t.kind === 'player' && t.alive);
  if (players.length && Math.random() < (tank.boss ? 0.6 : 0.45)) {
    let best = players[0], bd = Infinity;
    for (const p of players) {
      const d = Math.abs(p.x - tank.x) + Math.abs(p.y - tank.y);
      if (d < bd) { bd = d; best = p; }
    }
    return { x: best.x, y: best.y };
  }
  const c = world.baseCells[0] ?? 0;
  return { x: (c % GRID) * TILE, y: Math.floor(c / GRID) * TILE };
}

function dirTowards(tank, target) {
  const dx = target.x - tank.x, dy = target.y - tank.y;
  if (Math.abs(dx) > Math.abs(dy)) return [dx > 0 ? 1 : 3, dy > 0 ? 2 : 0];
  return [dy > 0 ? 2 : 0, dx > 0 ? 1 : 3];
}

function chooseDir(world, tank) {
  const open = [0, 1, 2, 3].filter((d) => canGo(world, tank, d));
  if (!open.length) return tank.dir;
  if (Math.random() < 0.7) {
    const [a, b] = dirTowards(tank, chooseTarget(world, tank));
    if (open.includes(a)) return a;
    if (open.includes(b)) return b;
  }
  return pick(open);
}

// Está com alguém (ou com a base) na linha de tiro?
function hasShot(world, tank) {
  const [dx, dy] = DIRV[tank.dir];
  const cx = tank.x + 8, cy = tank.y + 8;
  for (const t of world.tanks) {
    if (!t.alive || t.team === tank.team) continue;
    const ox = t.x + 8, oy = t.y + 8;
    if (dx !== 0 && Math.abs(oy - cy) < 12 && (ox - cx) * dx > 0) return true;
    if (dy !== 0 && Math.abs(ox - cx) < 12 && (oy - cy) * dy > 0) return true;
  }
  if (world.baseAlive && world.baseCells.length) {
    const c = world.baseCells[0];
    const bx = (c % GRID) * TILE + 8, by = Math.floor(c / GRID) * TILE + 8;
    if (dx !== 0 && Math.abs(by - cy) < 12 && (bx - cx) * dx > 0) return true;
    if (dy !== 0 && Math.abs(bx - cx) < 12 && (by - cy) * dy > 0) return true;
  }
  return false;
}

// Preenche `inputs` com o que cada inimigo quer fazer neste tick.
export function enemyTurn(world, inputs) {
  if (world.freeze > 0) return;        // Relógio: ninguém pensa nem atira

  for (const tank of world.tanks) {
    if (tank.kind !== 'enemy' || !tank.alive) continue;
    const ai = (tank.ai ||= { think: 0, fire: 60, dir: tank.dir });

    if (--ai.think <= 0 || !canGo(world, tank, ai.dir)) {
      ai.think = 25 + rnd(70);
      ai.dir = chooseDir(world, tank);
    }

    let fire = false;
    if (--ai.fire <= 0) { fire = true; ai.fire = 35 + rnd(55); }
    else if (hasShot(world, tank) && ai.fire < 25) { fire = true; ai.fire = 35 + rnd(40); }

    // Barragem do chefe: quatro balas de uma vez, uma pra cada lado.
    if (tank.boss && --ai.barrage <= 0) {
      ai.barrage = BOSS.barrageEvery;
      for (let d = 0; d < 4; d++) fireBullet(world, tank, d, { speed: 2.4 });
      world.fx.push({ k: 'barragem', x: tank.x + 8, y: tank.y + 8 });
      fire = false;
    }

    inputs[tank.id] = { dir: ai.dir, move: true, fire };
  }
}
