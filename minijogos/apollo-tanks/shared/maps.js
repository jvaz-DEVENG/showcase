// Mapas em texto: 26 linhas de 26 caracteres, cada caractere = 1 célula de 8u.
//   .  vazio        b  tijolo (destrutível)   s  aço (só tiro perfurante)
//   w  água (bloqueia tanque, tiro passa)     t  mato (esconde, tudo passa)
//   g  gelo (passa, mas o tanque desliza)     B  base (a águia)
//
// Regras de desenho que todo mapa tem que respeitar (o teste de fumaça cobra):
//   · a águia mora sempre nas células (12..13, 24..25), com muro de tijolo;
//   · os 4 pontos de nascimento dos jogadores (colunas 4,8,16,20 na linha 24)
//     e os 3 dos inimigos (colunas 0,12,24 na linha 0) ficam livres;
//   · dá pra ir de qualquer nascimento até a águia sem quebrar nada.

import { GRID, T } from './constants.js';

const CHAR = { '.': T.EMPTY, b: T.BRICK, s: T.STEEL, w: T.WATER, t: T.TREES, g: T.ICE, B: T.BASE };

// Fase 1 — o quintal de casa. Colunas soltas, muito espaço pra aprender.
const FORTALEZA = [
  '..........................',
  '..........................',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..ssssss..bb..bb..',
  '..bb..bb..ssssss..bb..bb..',
  '..........................',
  '..........bbbbbb..........',
  '..........bb..bb..........',
  '..bbbbbb..bb..bb..bbbbbb..',
  '..bbbbbb..bb..bb..bbbbbb..',
  '..........bb..bb..........',
  '..........bbbbbb..........',
  '..........................',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..........bb..bb..',
  '..bb..bb..........bb..bb..',
  '..........bbbbbb..........',
  '..........bbBBbb..........',
  '..........bbBBbb..........',
];

// Fase 2 — o pântano. Água corta o caminho mas deixa o tiro passar, e as
// faixas de mato escondem quem está dentro: dá pra emboscar e ser emboscado.
const PANTANO = [
  '..........................',
  '..........................',
  '..bbbb..bbbbbbbbbb..bbbb..',
  '..bbbb..bbbbbbbbbb..bbbb..',
  '..........................',
  '..........................',
  '..ssss..wwwwwwwwww..ssss..',
  '..ssss..wwwwwwwwww..ssss..',
  '..........................',
  '..........................',
  'bbbb..tttttttttttttt..bbbb',
  'bbbb..tttttttttttttt..bbbb',
  '..........................',
  '..........................',
  '..bbbb..ssssssssss..bbbb..',
  '..bbbb..ssssssssss..bbbb..',
  '..gggg..............gggg..',
  '..gggg..............gggg..',
  '..wwww..bbbbbbbbbb..wwww..',
  '..wwww..bbbbbbbbbb..wwww..',
  '..........................',
  '..........................',
  '..........................',
  '..........bbbbbb..........',
  '..........bbBBbb..........',
  '..........bbBBbb..........',
];

// Fase 3 — a usina. Caixas de aço que só o tiro perfurante abre, e um miolo
// blindado no meio do campo: quem não tem furo aprende a contornar.
const USINA = [
  '..........................',
  '..........................',
  '..ssss..bbbbbbbbbb..ssss..',
  '..s..s..b........b..s..s..',
  '..s..s..b..ssss..b..s..s..',
  '..ssss..b..s..s..b..ssss..',
  '........b..s..s..b........',
  'bbbb....b..ssss..b....bbbb',
  'b..b....b........b....b..b',
  'b..b....bbbb..bbbb....b..b',
  'bbbb..................bbbb',
  '..........................',
  '..wwww..ssss..ssss..wwww..',
  '..wwww..ssss..ssss..wwww..',
  '..........................',
  'bbbb..................bbbb',
  'b..b....bbbbbbbbbb....b..b',
  'b..b....b........b....b..b',
  'bbbb....b..tttt..b....bbbb',
  '........b..tttt..b........',
  '..ssss..b........b..ssss..',
  '..ssss..bbbb..bbbb..ssss..',
  '..........................',
  '..........bbbbbb..........',
  '..........bbBBbb..........',
  '..........bbBBbb..........',
];

// Fase 4 — o labirinto. Corredor apertado, pouca linha de tiro longa: aqui
// quem anda sem olhar esbarra num canhão.
const LABIRINTO = [
  '..........................',
  '..........................',
  '..bb..bb..bb..bb..bb..bb..',
  '..bb..bb..bb..bb..bb..bb..',
  '..........................',
  '..........................',
  'bbbb..bbbb..bb..bbbb..bbbb',
  'bbbb..bbbb..bb..bbbb..bbbb',
  '..........................',
  '..........................',
  '..ssss..bbbb..bbbb..ssss..',
  '..ssss..bbbb..bbbb..ssss..',
  '......gggggggggggggg......',
  '......gggggggggggggg......',
  'bb..bbbb..bbbbbb..bbbb..bb',
  'bb..bbbb..bbbbbb..bbbb..bb',
  '..........................',
  '..........................',
  '..bbbb..bb..bb..bb..bbbb..',
  '..bbbb..bb..bb..bb..bbbb..',
  '..........................',
  '..........................',
  '..........................',
  '..........bbbbbb..........',
  '..........bbBBbb..........',
  '..........bbBBbb..........',
];

// Fase 5 — a cratera, onde o Colosso espera. Faixa de mato atravessando o
// campo inteiro e dois pátios abertos: é o mapa mais exposto da campanha.
const CRATERA = [
  '..........................',
  '..........................',
  '..ssss..............ssss..',
  '..s..s..bbbbbbbbbb..s..s..',
  '..s..s..b........b..s..s..',
  '..ssss..b..wwww..b..ssss..',
  '........b..wwww..b........',
  '..bbbb..b........b..bbbb..',
  '..b..b..bbbb..bbbb..b..b..',
  '..b..b..............b..b..',
  '..bbbb..............bbbb..',
  '..........................',
  'ssss....tttttttttt....ssss',
  'ssss....tttttttttt....ssss',
  '..gggg..............gggg..',
  '..bbbb..............bbbb..',
  '..b..b..bbbb..bbbb..b..b..',
  '..b..b..b........b..b..b..',
  '..bbbb..b..ssss..b..bbbb..',
  '........b..ssss..b........',
  '..wwww..b........b..wwww..',
  '..wwww..bbbb..bbbb..wwww..',
  '..........................',
  '..........bbbbbb..........',
  '..........bbBBbb..........',
  '..........bbBBbb..........',
];

export const MAPS = [
  { id: 'stage1', name: 'Fortaleza', tag: 'aquecimento', rows: FORTALEZA },
  { id: 'stage2', name: 'Pântano', tag: 'água e emboscada', rows: PANTANO },
  { id: 'stage3', name: 'Usina', tag: 'aço por todo lado', rows: USINA },
  { id: 'stage4', name: 'Labirinto', tag: 'corredor apertado', rows: LABIRINTO },
  { id: 'stage5', name: 'Cratera', tag: 'o Colosso', rows: CRATERA },
];

// Falha alto e cedo se algum mapa estiver com linha fora do tamanho.
for (const m of MAPS) {
  if (m.rows.length !== GRID) throw new Error(`mapa ${m.id}: ${m.rows.length} linhas, esperado ${GRID}`);
  m.rows.forEach((row, i) => {
    if (row.length !== GRID) throw new Error(`mapa ${m.id} linha ${i}: ${row.length} colunas, esperado ${GRID}`);
    for (const c of row) if (!(c in CHAR)) throw new Error(`mapa ${m.id} linha ${i}: caractere inválido "${c}"`);
  });
}

export function parseMap(rows) {
  const tiles = new Uint8Array(GRID * GRID);
  for (let y = 0; y < GRID; y++)
    for (let x = 0; x < GRID; x++) tiles[y * GRID + x] = CHAR[rows[y][x]];
  return tiles;
}

// Células de tijolo coladas na base — é o que o Suporte reconstrói.
export function baseWallCells(tiles) {
  const out = [];
  for (let y = 0; y < GRID; y++) {
    for (let x = 0; x < GRID; x++) {
      if (tiles[y * GRID + x] !== T.BRICK) continue;
      let nearBase = false;
      for (let dy = -2; dy <= 2 && !nearBase; dy++)
        for (let dx = -2; dx <= 2; dx++) {
          const nx = x + dx, ny = y + dy;
          if (nx < 0 || ny < 0 || nx >= GRID || ny >= GRID) continue;
          if (tiles[ny * GRID + nx] === T.BASE) { nearBase = true; break; }
        }
      if (nearBase) out.push(y * GRID + x);
    }
  }
  return out;
}

export function findBaseCells(tiles) {
  const out = [];
  for (let i = 0; i < tiles.length; i++) if (tiles[i] === T.BASE) out.push(i);
  return out;
}
