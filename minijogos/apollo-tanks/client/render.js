// Desenho do campo. Tudo é feito por código (nada de sprites em arquivo),
// num canvas de 208x208 esticado por CSS — por isso o visual pixelado do NES.
//
// Camadas, de baixo pra cima:
//   chão + tijolo + aço + gelo → canvas escondido, repintado só quando muda
//   água (animada) · águia · minas · bônus · balas · tanques · sentinelas ·
//   partículas · mato · congelamento · vinheta
//
// Todas as medidas dos desenhos são inteiras de propósito: meio pixel num
// canvas de 208 vira borrão.

import { TILE, GRID, FIELD, TANK, T, PA_AVISO, BONUS } from '../shared/constants.js';

// Ruído estável por célula: o mesmo tijolo sai igual em todo mundo e não
// pisca entre um quadro e outro.
function ruido(x, y) {
  let h = (x * 374761393 + y * 668265263) ^ 0x5bf03635;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

// Cada classe e cada tipo de inimigo tem uma silhueta própria: largura do
// casco, tamanho da torre e comprimento do cano mudam o suficiente pra você
// reconhecer quem é quem no meio do tiroteio.
const FORMAS = {
  assalto: { casco: 8, torre: 6, canoTip: -9, canoW: 2 },
  pesado: { casco: 10, torre: 8, canoTip: -8, canoW: 4, aba: true },
  sniper: { casco: 8, torre: 6, canoTip: -12, canoW: 2 },
  suporte: { casco: 10, torre: 6, canoTip: -9, canoW: 2, antena: true },
  // torre larga e cano curto: o basico era clone exato do assalto e os dois
  // só se distinguiam pela cor.
  basico: { casco: 8, torre: 8, canoTip: -8, canoW: 2 },
  veloz: { casco: 8, torre: 6, canoTip: -9, canoW: 2, aleta: true },
  canhao: { casco: 10, torre: 6, canoTip: -10, canoW: 4 },
  blindado: { casco: 10, torre: 8, canoTip: -8, canoW: 2, aba: true },
  colosso: { casco: 10, torre: 8, canoTip: -11, canoW: 4, aba: true, espinho: true },
};

// Padrão pintado no casco. A cor continua sendo a do lugar na sala — a skin só
// muda o desenho por cima, senão ninguém sabe mais quem é quem.
function pintarSkin(ctx, skin, cor, meio) {
  const esq = -meio, larg = meio * 2;
  if (skin === 'listras') {
    ctx.fillStyle = shade(cor, -0.4);
    ctx.fillRect(esq + 1, -7, 1, 14);
    ctx.fillRect(meio - 2, -7, 1, 14);
  } else if (skin === 'xadrez') {
    ctx.fillStyle = shade(cor, -0.4);
    for (let y = -7; y < 7; y += 4) {
      for (let x = esq; x < meio; x += 4) {
        if (((x - esq) / 2 + (y + 7) / 2) % 4 < 2) continue;
        ctx.fillRect(x, y, 2, 2);
      }
    }
  } else if (skin === 'camuflado') {
    ctx.fillStyle = shade(cor, -0.35);
    ctx.fillRect(esq + 1, -6, 3, 3);
    ctx.fillRect(meio - 4, -2, 3, 4);
    ctx.fillRect(esq + 2, 3, 4, 2);
  } else if (skin === 'tigre') {
    ctx.fillStyle = shade(cor, -0.5);
    ctx.fillRect(esq, -5, larg, 1);
    ctx.fillRect(esq, -1, larg, 1);
    ctx.fillRect(esq, 3, larg, 1);
  } else if (skin === 'veterano') {
    ctx.fillStyle = shade(cor, 0.55);
    ctx.fillRect(esq, -7, larg, 1);
    ctx.fillRect(esq, 6, larg, 1);
    ctx.fillStyle = '#e5484d';
    ctx.fillRect(esq + 1, 4, 2, 1);
  }
}

// Desenha um tanque em volta da origem, com o cano pra cima. Usada no jogo e
// na prévia da skin no perfil.
export function pintarTanque(ctx, t, time = 0) {
  const forma = FORMAS[t.etype] || FORMAS[t.cls] || FORMAS.basico;
  const meio = forma.casco / 2;
  const tm = forma.torre / 2;

  // Inimigo portador de bônus pisca entre a cor dele e o branco, como no NES.
  const cor = t.portador && Math.floor(time / 130) % 2 ? '#f4f6fa' : t.color;

  // ---- casco, com contorno escuro pra destacar do chão
  ctx.fillStyle = '#0a0d12';
  ctx.fillRect(-meio - 1, -8, forma.casco + 2, 16);
  ctx.fillStyle = cor;
  ctx.fillRect(-meio, -7, forma.casco, 14);
  ctx.fillStyle = shade(cor, -0.45);           // traseira na sombra
  ctx.fillRect(-meio, 3, forma.casco, 4);
  ctx.fillStyle = shade(cor, 0.4);             // chanfro na frente
  ctx.fillRect(-meio + 1, -7, forma.casco - 2, 1);
  pintarSkin(ctx, t.skin, cor, meio);

  if (forma.aba) {                              // saias de blindagem
    ctx.fillStyle = shade(cor, -0.3);
    ctx.fillRect(-meio, -5, 1, 10);
    ctx.fillRect(meio - 1, -5, 1, 10);
  }

  // ---- esteiras por cima do casco: ficam sempre visíveis
  const tread = Math.floor(time / 60) % 2;
  ctx.fillStyle = '#20242b';
  ctx.fillRect(-8, -8, 3, 16);
  ctx.fillRect(5, -8, 3, 16);
  ctx.fillStyle = '#767c86';
  for (let i = -8 + (t.moving ? tread * 2 : 0); i < 8; i += 4) {
    ctx.fillRect(-8, i, 3, 1);
    ctx.fillRect(5, i, 3, 1);
  }
  ctx.fillStyle = '#454b56';                    // roda-guia na frente e atrás
  ctx.fillRect(-8, -8, 3, 1);
  ctx.fillRect(5, -8, 3, 1);
  ctx.fillRect(-8, 7, 3, 1);
  ctx.fillRect(5, 7, 3, 1);

  if (forma.aleta) {                            // aletas do veloz
    ctx.fillStyle = shade(cor, 0.5);
    ctx.fillRect(-8, 3, 3, 1);
    ctx.fillRect(5, 3, 3, 1);
  }
  if (forma.espinho) {                          // espigões do chefe
    ctx.fillStyle = '#20242b';
    ctx.fillRect(-9, -6, 1, 3);
    ctx.fillRect(8, -6, 1, 3);
    ctx.fillRect(-9, 3, 1, 3);
    ctx.fillRect(8, 3, 1, 3);
  }

  // ---- cano, desenhado ANTES da torre: ele tem que nascer por baixo dela,
  // senão vira uma coluna cinza cobrindo o tanque inteiro.
  const cw = forma.canoW, cx0 = -cw / 2;
  const comp = -forma.canoTip - tm + 1;
  ctx.fillStyle = '#2b3037';
  ctx.fillRect(cx0, forma.canoTip, cw, comp);
  ctx.fillStyle = '#6a7280';
  ctx.fillRect(cx0, forma.canoTip, 1, comp);
  ctx.fillStyle = '#aeb6c2';                    // boca do cano
  ctx.fillRect(cx0, forma.canoTip, cw, 1);

  // ---- torre. Sem contorno por fora: ela tem quase a largura do casco, e uma
  // moldura escura em volta apagaria o corpo do tanque.
  ctx.fillStyle = shade(cor, -0.2);
  ctx.fillRect(-tm, -tm, forma.torre, forma.torre);
  ctx.fillStyle = shade(cor, 0.34);
  ctx.fillRect(-tm, -tm, forma.torre, 1);
  ctx.fillStyle = shade(cor, -0.45);
  ctx.fillRect(tm - 1, -tm, 1, forma.torre);
  ctx.fillStyle = '#0a0d12';
  ctx.fillRect(-tm, tm - 1, forma.torre, 1);
  if (forma.espinho) {                          // núcleo pulsante do chefe
    ctx.fillStyle = Math.floor(time / 160) % 2 ? '#ffd166' : '#ff6b3d';
    ctx.fillRect(-2, -2, 4, 4);
  }

  if (forma.antena) {                           // antena do suporte
    ctx.fillStyle = '#8ce07a';
    ctx.fillRect(meio - 1, -tm - 5, 1, 5);
    ctx.fillRect(meio - 2, -tm - 6, 2, 1);
  }
}

export function createRenderer(canvas) {
  const ctx = canvas.getContext('2d');
  ctx.imageSmoothingEnabled = false;

  // Camada estática (chão, tijolo, aço e gelo).
  const layer = document.createElement('canvas');
  layer.width = FIELD;
  layer.height = FIELD;
  const lctx = layer.getContext('2d');

  // Quem pediu menos movimento no sistema não leva tremida de tela. O resto do
  // campo continua animado: aquilo é o jogo, não enfeite.
  const semTremor = matchMedia('(prefers-reduced-motion: reduce)').matches;

  let tiles = new Uint8Array(GRID * GRID);
  let aguas = [];        // células de água, pré-listadas (a água anima todo quadro)
  let matos = [];        // idem pro mato, que é desenhado por cima de tudo
  let murosBase = [];    // muro da águia, pra piscar quando a Pá vai acabar
  const particles = [];
  let shake = 0;

  // ------------------------------------------------------------- tiles

  function paintCell(cx, cy) {
    const x = cx * TILE, y = cy * TILE;
    const t = tiles[cy * GRID + cx];
    const n = ruido(cx, cy);

    // chão: cinza bem escuro com sujeira, pra não ficar aquele preto chapado
    lctx.fillStyle = '#101319';
    lctx.fillRect(x, y, TILE, TILE);
    if (n > 0.82) {
      lctx.fillStyle = '#161b23';
      lctx.fillRect(x + ((n * 7) | 0), y + ((n * 13) % 7 | 0), 2, 1);
    }

    if (t === T.BRICK) {
      // argamassa por baixo, dois tijolos desencontrados por cima
      lctx.fillStyle = '#4a1c0b';
      lctx.fillRect(x, y, TILE, TILE);
      const off = (cy % 2) ? 4 : 0;
      for (let linha = 0; linha < 2; linha++) {
        const ty = y + linha * 4;
        lctx.fillStyle = n > 0.6 ? '#d96820' : n > 0.3 ? '#c85f1c' : '#b9551a';
        lctx.fillRect(x, ty, TILE, 3);
        // topo mais claro e base mais escura dão volume ao tijolo
        lctx.fillStyle = '#e88a3f';
        lctx.fillRect(x, ty, TILE, 1);
        lctx.fillStyle = '#8d3f13';
        lctx.fillRect(x, ty + 2, TILE, 1);
        // junta vertical
        lctx.fillStyle = '#4a1c0b';
        lctx.fillRect(x + ((off + linha * 4) % TILE), ty, 1, 3);
      }
    } else if (t === T.STEEL) {
      lctx.fillStyle = '#5d646f';
      lctx.fillRect(x, y, TILE, TILE);
      lctx.fillStyle = '#aeb6c2';
      lctx.fillRect(x, y, TILE - 1, TILE - 1);
      lctx.fillStyle = '#dde3ea';                 // brilho no canto superior
      lctx.fillRect(x, y, TILE - 1, 1);
      lctx.fillRect(x, y, 1, TILE - 1);
      lctx.fillStyle = '#7d8590';
      lctx.fillRect(x + 2, y + 2, 4, 4);
      lctx.fillStyle = '#eef2f7';                 // rebite
      lctx.fillRect(x + 3, y + 3, 2, 2);
      lctx.fillStyle = '#3d434c';
      lctx.fillRect(x + TILE - 1, y + 1, 1, TILE - 1);
      lctx.fillRect(x + 1, y + TILE - 1, TILE - 1, 1);
    } else if (t === T.ICE) {
      lctx.fillStyle = '#9fc8e8';
      lctx.fillRect(x, y, TILE, TILE);
      lctx.fillStyle = '#cfe6f7';
      lctx.fillRect(x, y, TILE - 1, TILE - 1);
      lctx.fillStyle = '#ffffff';                 // rachaduras
      lctx.fillRect(x + 1, y + 2, 3, 1);
      lctx.fillRect(x + 4, y + 5, 3, 1);
      lctx.fillStyle = '#7fb0d6';
      lctx.fillRect(x, y + TILE - 1, TILE, 1);
      lctx.fillRect(x + TILE - 1, y, 1, TILE);
    }
  }

  function indexar() {
    aguas = [];
    matos = [];
    murosBase = [];
    let temBase = false;
    for (let i = 0; i < tiles.length; i++) if (tiles[i] === T.BASE || tiles[i] === T.BASE_DEAD) temBase = true;
    for (let cy = 0; cy < GRID; cy++) {
      for (let cx = 0; cx < GRID; cx++) {
        const t = tiles[cy * GRID + cx];
        if (t === T.WATER) aguas.push([cx, cy]);
        else if (t === T.TREES) matos.push([cx, cy]);
        else if (temBase && cy >= GRID - 3 && cx >= 10 && cx <= 15 && (t === T.BRICK || t === T.STEEL)) {
          murosBase.push([cx, cy]);
        }
      }
    }
  }

  function repaintAll() {
    lctx.clearRect(0, 0, FIELD, FIELD);
    for (let cy = 0; cy < GRID; cy++) for (let cx = 0; cx < GRID; cx++) paintCell(cx, cy);
    indexar();
  }

  function setTiles(arr) {
    tiles = arr instanceof Uint8Array ? arr : Uint8Array.from(arr);
    repaintAll();
  }

  // dt vem como [indice, tipo, indice, tipo, ...]
  function patchTiles(dt) {
    let reindexar = false;
    for (let i = 0; i < dt.length; i += 2) {
      const cell = dt[i];
      const antes = tiles[cell];
      tiles[cell] = dt[i + 1];
      if (antes !== dt[i + 1]) reindexar = true;
      paintCell(cell % GRID, Math.floor(cell / GRID));
    }
    if (reindexar) indexar();
  }

  function reset() {
    particles.length = 0;
    shake = 0;
  }

  // ------------------------------------------------------------- partículas

  // Teto de partículas. Os efeitos chegam pela rede (que nunca para) mas só
  // envelhecem dentro do draw(), que congela junto com o requestAnimationFrame
  // quando a aba vai pro fundo. Sem teto, o array cresceria o tempo todo.
  const MAX_PARTICULAS = 600;
  const solta = (p) => {
    if (particles.length >= MAX_PARTICULAS) particles.shift();
    particles.push(p);
  };

  function faisca(x, y, cor, forca, n, decay) {
    for (let i = 0; i < n; i++) {
      const a = Math.random() * Math.PI * 2;
      const s = forca * (0.4 + Math.random());
      solta({ tipo: 'ponto', x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, life: 1, decay, c: cor, r: 1 });
    }
  }

  function spawnFx(fx) {
    for (const f of fx) {
      if (f.k === 'boom') {
        const grande = !!f.big;
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 2, vr: grande ? 1.5 : 0.9, life: 1, decay: grande ? 0.05 : 0.08, c: '#ffd166' });
        for (let i = 0; i < (grande ? 40 : 18); i++) {
          const a = Math.random() * Math.PI * 2;
          const s = (grande ? 1.7 : 1.1) * (0.4 + Math.random());
          solta({
            tipo: 'ponto', x: f.x, y: f.y, vx: Math.cos(a) * s, vy: Math.sin(a) * s,
            life: 1, decay: 0.02 + Math.random() * 0.02,
            c: ['#fff1c2', '#ff8c42', '#e5484d'][i % 3], r: grande ? 2 : 1.5,
          });
        }
        // fumaça sobe devagar depois do estouro
        for (let i = 0; i < (grande ? 12 : 5); i++) {
          const a = Math.random() * Math.PI * 2;
          solta({
            tipo: 'ponto', x: f.x, y: f.y, vx: Math.cos(a) * 0.35, vy: Math.sin(a) * 0.35 - 0.25,
            life: 1, decay: 0.014, c: '#5a5f6b', r: 2,
          });
        }
        if (!semTremor) shake = Math.max(shake, grande ? 7 : 2.5);
      } else if (f.k === 'ping') {
        faisca(f.x, f.y, '#ffe9b0', 0.7, 5, 0.09);
      } else if (f.k === 'spark') {
        faisca(f.x, f.y, '#9fd8ff', 1.1, 8, 0.06);
      } else if (f.k === 'shot') {
        solta({ tipo: 'clarao', x: f.x, y: f.y, d: f.d ?? 0, life: 1, decay: 0.34, c: '#fff3c4' });
      } else if (f.k === 'spawn') {
        for (let i = 0; i < 14; i++) {
          const a = (i / 14) * Math.PI * 2;
          solta({
            tipo: 'ponto', x: f.x + Math.cos(a) * 11, y: f.y + Math.sin(a) * 11,
            vx: -Math.cos(a) * 0.7, vy: -Math.sin(a) * 0.7, life: 1, decay: 0.045, c: '#ffffff', r: 1,
          });
        }
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 12, vr: -1.1, life: 1, decay: 0.06, c: '#cfe6ff' });
      } else if (f.k === 'hab') {
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 3, vr: 1.3, life: 1, decay: 0.055, c: f.c || '#f2c14e' });
      } else if (f.k === 'rastro') {
        solta({ tipo: 'ponto', x: f.x, y: f.y, vx: 0, vy: 0, life: 0.7, decay: 0.06, c: f.c || '#f2c14e', r: 2 });
      } else if (f.k === 'barragem') {
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 2, vr: 2.2, life: 1, decay: 0.045, c: '#e5484d' });
        faisca(f.x, f.y, '#ff9f45', 1.4, 14, 0.05);
        if (!semTremor) shake = Math.max(shake, 4);
      } else if (f.k === 'chefe') {
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 2, vr: 2.6, life: 1, decay: 0.03, c: '#e5484d' });
        if (!semTremor) shake = Math.max(shake, 6);
      } else if (f.k === 'bonus') {
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 2, vr: 1.1, life: 1, decay: 0.05, c: '#ffe9b0' });
      } else if (f.k === 'pegou') {
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 3, vr: 1.6, life: 1, decay: 0.05, c: '#8ce07a' });
        faisca(f.x, f.y, '#8ce07a', 1.2, 12, 0.05);
      } else if (f.k === 'granada') {
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 4, vr: 6, life: 1, decay: 0.03, c: '#ffd166' });
        if (!semTremor) shake = Math.max(shake, 8);
      } else if (f.k === 'vida') {
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 2, vr: 0.9, life: 1, decay: 0.04, c: '#8ce07a' });
      } else if (f.k === 'aguia') {
        // a blindagem comeu um tiro: anel dourado em volta da águia
        solta({ tipo: 'anel', x: f.x, y: f.y, r: 6, vr: 1.4, life: 1, decay: 0.06, c: '#f2c14e' });
        if (!semTremor) shake = Math.max(shake, 3);
      }
    }
  }

  function stepParticles() {
    for (let i = particles.length - 1; i >= 0; i--) {
      const p = particles[i];
      if (p.tipo === 'anel') p.r += p.vr;
      else if (p.tipo === 'ponto') { p.x += p.vx; p.y += p.vy; p.vx *= 0.94; p.vy *= 0.94; }
      p.life -= p.decay;
      if (p.life <= 0) particles.splice(i, 1);
    }
    if (shake > 0) shake = Math.max(0, shake - 0.45);
  }

  function drawParticles() {
    for (const p of particles) {
      ctx.globalAlpha = Math.max(0, Math.min(1, p.life));
      ctx.fillStyle = p.c;
      if (p.tipo === 'anel') {
        ctx.strokeStyle = p.c;
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(p.x, p.y, Math.max(0.5, p.r), 0, Math.PI * 2);
        ctx.stroke();
      } else if (p.tipo === 'clarao') {
        const [dx, dy] = [[0, -1], [1, 0], [0, 1], [-1, 0]][p.d];
        ctx.fillRect(Math.round(p.x - 2 + dx), Math.round(p.y - 2 + dy), 4, 4);
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(Math.round(p.x - 1), Math.round(p.y - 1), 2, 2);
      } else {
        ctx.fillRect(Math.round(p.x - p.r), Math.round(p.y - p.r), p.r * 2, p.r * 2);
      }
    }
    ctx.globalAlpha = 1;
  }

  // ------------------------------------------------------------- terreno vivo

  function drawWater(time) {
    const fase = Math.floor(time / 320) % 3;
    for (const [cx, cy] of aguas) {
      const x = cx * TILE, y = cy * TILE;
      ctx.fillStyle = '#0e2f5c';
      ctx.fillRect(x, y, TILE, TILE);
      ctx.fillStyle = '#194a8f';
      ctx.fillRect(x, y + 1, TILE, 3);
      ctx.fillStyle = '#3e86d6';
      const o = [0, 3, 5][fase];
      ctx.fillRect(x + (o % TILE), y + 2, 3, 1);
      ctx.fillRect(x + ((o + 4) % TILE), y + 5, 2, 1);
      ctx.fillStyle = '#8fd0ff';
      ctx.fillRect(x + ((o + 2) % TILE), y + 2, 1, 1);
    }
  }

  function drawTrees(time) {
    const balanco = Math.floor(time / 500) % 2;
    for (const [cx, cy] of matos) {
      const x = cx * TILE, y = cy * TILE;
      const n = ruido(cx, cy);
      ctx.fillStyle = '#0d3316';
      ctx.fillRect(x, y, TILE, TILE);
      ctx.fillStyle = n > 0.5 ? '#1f6d2b' : '#256f31';
      ctx.fillRect(x + 1, y, 3, 4);
      ctx.fillRect(x + 4, y + 3, 3, 4);
      ctx.fillRect(x, y + 5, 3, 3);
      ctx.fillStyle = '#3fa04d';
      ctx.fillRect(x + 1 + balanco, y + 1, 2, 2);
      ctx.fillRect(x + 5, y + 4, 1, 2);
      ctx.fillStyle = '#5fc46d';
      ctx.fillRect(x + 2, y + 1, 1, 1);
    }
  }

  // Enquanto a Pá dura, o muro da águia fica azulado; nos últimos 3s, piscando.
  function drawPa(pa, time) {
    if (!pa) return;
    if (pa < PA_AVISO && Math.floor(time / 110) % 2) return;
    ctx.globalAlpha = 0.35;
    ctx.fillStyle = '#9fd8ff';
    for (const [cx, cy] of murosBase) ctx.fillRect(cx * TILE, cy * TILE, TILE, TILE);
    ctx.globalAlpha = 1;
  }

  function drawBase(alive, time, hp = 1, hpMax = 1) {
    let minX = 1e9, minY = 1e9;
    let found = false;
    for (let i = 0; i < tiles.length; i++) {
      if (tiles[i] !== T.BASE && tiles[i] !== T.BASE_DEAD) continue;
      found = true;
      minX = Math.min(minX, (i % GRID) * TILE);
      minY = Math.min(minY, Math.floor(i / GRID) * TILE);
    }
    if (!found) return;

    ctx.save();
    ctx.translate(minX, minY);
    if (!alive) {
      ctx.fillStyle = '#1a1a1a';
      ctx.fillRect(0, 0, TANK, TANK);
      ctx.fillStyle = '#3a3a3a';                      // escombro
      ctx.fillRect(1, 10, 14, 5);
      ctx.fillRect(3, 8, 4, 2);
      const chama = Math.floor(time / 140) % 3;
      ctx.fillStyle = ['#e5484d', '#ff8c42', '#ffd166'][chama];
      ctx.fillRect(6, 3 + chama, 4, 6 - chama);
      ctx.fillStyle = '#ffe9b0';
      ctx.fillRect(7, 5 + chama, 2, 2);
    } else {
      // A águia do NES: cabeça pequena, asas abertas em degrau, cauda curta —
      // tudo em blocos inteiros pra ler bem a 8 pixels de altura.
      ctx.fillStyle = '#f0f2f5';
      ctx.fillRect(7, 1, 2, 2);            // cabeça
      ctx.fillRect(6, 3, 4, 8);            // corpo
      ctx.fillRect(1, 4, 3, 3);            // ponta da asa esquerda, erguida
      ctx.fillRect(12, 4, 3, 3);           // ponta da asa direita
      ctx.fillRect(3, 6, 10, 2);           // meio das asas, mais baixo
      ctx.fillRect(4, 11, 8, 1);           // cauda

      ctx.fillStyle = '#aab1bd';           // sombra por baixo das penas
      ctx.fillRect(1, 6, 3, 1);
      ctx.fillRect(12, 6, 3, 1);
      ctx.fillRect(3, 7, 10, 1);
      ctx.fillRect(6, 10, 4, 1);

      ctx.fillStyle = '#20242b';           // olho
      ctx.fillRect(7, 2, 1, 1);
      ctx.fillStyle = '#e8a020';           // bico
      ctx.fillRect(9, 2, 2, 1);

      // pedestal
      ctx.fillStyle = '#8a6a12';
      ctx.fillRect(1, 14, 14, 2);
      ctx.fillStyle = '#c9a227';
      ctx.fillRect(1, 12, 14, 2);
      ctx.fillStyle = '#e8c552';
      ctx.fillRect(2, 12, 12, 1);

      // A blindagem comprada vira anéis em volta: um por tiro que ela aguenta.
      for (let i = 0; i < hp - 1; i++) {
        ctx.globalAlpha = 0.5 + 0.2 * Math.sin(time / 300 + i);
        ctx.strokeStyle = '#8ce07a';
        ctx.lineWidth = 1;
        ctx.strokeRect(-1.5 - i * 2, -1.5 - i * 2, TANK + 3 + i * 4, TANK + 3 + i * 4);
      }
      if (hpMax === 1) {
        ctx.globalAlpha = 0.25 + 0.15 * Math.sin(time / 380);
        ctx.strokeStyle = '#f2c14e';
        ctx.lineWidth = 1;
        ctx.strokeRect(-1.5, -1.5, TANK + 3, TANK + 3);
      }
      ctx.globalAlpha = 1;
    }
    ctx.restore();
  }

  // ------------------------------------------------------------- peças vivas

  function drawTank(t, time) {
    // Tudo que enfeita o tanque tem que usar a MESMA posição arredondada do
    // corpo. Com o x/y interpolado (float), barra de vida, estrelas e anéis
    // borram e tremem em cima de um tanque que está nítido.
    const px = Math.round(t.x), py = Math.round(t.y);

    // sombra no chão, dá peso ao tanque
    ctx.globalAlpha = 0.3;
    ctx.fillStyle = '#000';
    ctx.fillRect(px + 1, py + TANK - 1, TANK - 2, 2);
    ctx.globalAlpha = 1;

    ctx.save();
    ctx.translate(px + TANK / 2, py + TANK / 2);
    ctx.rotate((t.dir * Math.PI) / 2);
    pintarTanque(ctx, t, time);
    ctx.restore();

    // vida acima do tanque, só quando aguenta mais de um tiro
    if (t.hpMax > 1 && t.hp > 0) {
      const larg = Math.max(2, Math.min(14, t.hpMax * 3));
      const by = Math.max(0, py - 3);          // não sai do campo na linha de cima
      ctx.fillStyle = '#20242b';
      ctx.fillRect(px + 1, by, larg, 2);
      ctx.fillStyle = t.boss ? '#e5484d' : '#8ce07a';
      ctx.fillRect(px + 1, by, Math.round(larg * (t.hp / t.hpMax)), 2);
    }

    // estrelas do nível do tanque, no canto de baixo
    if (t.level > 0) {
      ctx.fillStyle = '#ffd166';
      const sy = Math.min(FIELD - 2, py + TANK);
      for (let i = 0; i < t.level; i++) ctx.fillRect(px + 1 + i * 3, sy, 2, 2);
    }

    // "perfurar" carregado: o cano fica brilhando
    if (t.pierce) {
      ctx.globalAlpha = 0.5 + 0.4 * Math.sin(time / 90);
      ctx.strokeStyle = '#9fd8ff';
      ctx.lineWidth = 1;
      ctx.strokeRect(px + 0.5, py + 0.5, TANK - 1, TANK - 1);
      ctx.globalAlpha = 1;
    }

    if (t.shield) {
      const on = Math.floor(time / 60) % 2;
      ctx.strokeStyle = on ? '#9fd8ff' : '#ffffff';
      ctx.lineWidth = 1;
      ctx.strokeRect(px - 1.5, py - 1.5, TANK + 3, TANK + 3);
    }
  }

  function drawMarker(t, time) {
    const bob = Math.floor(time / 250) % 2;
    ctx.fillStyle = '#ffffff';
    const x = Math.round(t.x + 8), y = Math.round(t.y) - 5 - bob;
    ctx.fillRect(x - 2, y, 5, 1);
    ctx.fillRect(x - 1, y + 1, 3, 1);
    ctx.fillRect(x, y + 2, 1, 1);
  }

  function drawBullet(b) {
    const x = Math.round(b.x), y = Math.round(b.y);
    const [dx, dy] = [[0, -1], [1, 0], [0, 1], [-1, 0]][b.d ?? 0];

    // rastro curto atrás da bala
    ctx.globalAlpha = 0.35;
    ctx.fillStyle = b.pi ? '#9fd8ff' : b.p >= 2 ? '#ffd166' : '#cfd6e0';
    ctx.fillRect(x - dx * 3, y - dy * 3, 4, 4);
    ctx.globalAlpha = 0.6;
    ctx.fillRect(x - dx * 2, y - dy * 2, 4, 4);
    ctx.globalAlpha = 1;

    ctx.fillStyle = b.pi ? '#9fd8ff' : b.p >= 2 ? '#ffd166' : '#f2f2f2';
    ctx.fillRect(x, y, 4, 4);
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(x + 1, y + 1, 2, 2);
  }

  // Os seis bônus do original: caixa piscante com o desenho de cada um dentro.
  function drawBonus(b, time) {
    const x = Math.round(b.x), y = Math.round(b.y);
    if (b.l < 180 && Math.floor(time / 110) % 2) return;   // sumindo: pisca

    ctx.fillStyle = '#0b0d10';
    ctx.fillRect(x, y, TANK, TANK);
    ctx.strokeStyle = Math.floor(time / 180) % 2 ? '#ffd166' : '#ffffff';
    ctx.lineWidth = 1;
    ctx.strokeRect(x + 0.5, y + 0.5, TANK - 1, TANK - 1);

    const p = (px, py, w, h, c) => { ctx.fillStyle = c; ctx.fillRect(x + px, y + py, w, h); };
    if (b.k === 'capacete') {
      p(3, 6, 10, 5, '#9fd8ff'); p(4, 4, 8, 3, '#cfe9ff'); p(2, 10, 12, 2, '#6aa8d6');
    } else if (b.k === 'relogio') {
      p(3, 3, 10, 10, '#e8e8e8'); p(4, 4, 8, 8, '#2b3037'); p(7, 5, 2, 4, '#ffd166'); p(8, 8, 4, 2, '#ffd166');
    } else if (b.k === 'pa') {
      p(7, 2, 3, 7, '#c9a227'); p(5, 8, 7, 6, '#aeb6c2'); p(6, 9, 5, 4, '#e3e8ee');
    } else if (b.k === 'estrela') {
      p(7, 2, 3, 12, '#ffd166'); p(2, 6, 13, 3, '#ffd166'); p(4, 4, 9, 7, '#ffe9b0'); p(6, 5, 5, 6, '#ffd166');
    } else if (b.k === 'granada') {
      p(5, 5, 7, 8, '#3b414b'); p(6, 6, 5, 6, '#5f6875'); p(7, 2, 3, 4, '#c9a227'); p(9, 1, 4, 2, '#e5484d');
    } else if (b.k === 'tanque') {
      p(3, 5, 10, 7, '#8ce07a'); p(2, 6, 2, 5, '#4f9a45'); p(12, 6, 2, 5, '#4f9a45'); p(7, 2, 2, 4, '#2b3037');
    }
  }

  function drawMina(cel, time) {
    const x = (cel % GRID) * TILE, y = Math.floor(cel / GRID) * TILE;
    ctx.fillStyle = '#3b414b';
    ctx.fillRect(x + 1, y + 2, 6, 4);
    ctx.fillStyle = '#20242b';
    ctx.fillRect(x + 2, y + 3, 4, 2);
    ctx.fillStyle = Math.floor(time / 400) % 2 ? '#e5484d' : '#7a1c1f';
    ctx.fillRect(x + 3, y + 1, 2, 1);
  }

  function drawTorre(s, time) {
    const x = Math.round(s.x), y = Math.round(s.y);
    ctx.fillStyle = '#0a0d12';
    ctx.fillRect(x, y, TANK, TANK);
    ctx.fillStyle = '#4a5361';
    ctx.fillRect(x + 1, y + 1, TANK - 2, TANK - 2);
    ctx.fillStyle = '#79839a';
    ctx.fillRect(x + 1, y + 1, TANK - 2, 1);
    ctx.fillStyle = '#2b3037';
    ctx.fillRect(x + 4, y + 4, 8, 8);
    ctx.fillStyle = Math.floor(time / 300) % 2 ? '#8ce07a' : '#4f9a45';
    ctx.fillRect(x + 6, y + 6, 4, 4);

    // cano apontando pro último alvo
    const [dx, dy] = [[0, -1], [1, 0], [0, 1], [-1, 0]][s.d ?? 0];
    ctx.fillStyle = '#aeb6c2';
    ctx.fillRect(x + 7 + dx * 5, y + 7 + dy * 5, 2, 2);

    // vida da torre
    ctx.fillStyle = '#20242b';
    ctx.fillRect(x + 2, y - 2, 12, 2);
    ctx.fillStyle = '#8ce07a';
    ctx.fillRect(x + 2, y - 2, Math.round(12 * (s.h / 3)), 2);
  }

  // Relógio ativo: o campo inteiro ganha um véu gelado.
  function drawFreeze(freeze, time) {
    if (!freeze) return;
    if (freeze < 90 && Math.floor(time / 110) % 2) return;
    ctx.globalAlpha = 0.14;
    ctx.fillStyle = '#9fd8ff';
    ctx.fillRect(0, 0, FIELD, FIELD);
    ctx.globalAlpha = 0.5;
    ctx.fillStyle = '#dff1ff';
    for (let i = 0; i < 10; i++) {
      const n = ruido(i, Math.floor(time / 260) + i);
      ctx.fillRect(Math.floor(n * FIELD), Math.floor(((n * 37) % 1) * FIELD), 1, 1);
    }
    ctx.globalAlpha = 1;
  }

  // Escurece as bordas — só o suficiente pra tela ganhar profundidade.
  function drawVinheta() {
    for (let i = 0; i < 4; i++) {
      ctx.globalAlpha = 0.16 - i * 0.035;
      ctx.strokeStyle = '#000';
      ctx.lineWidth = 1;
      ctx.strokeRect(i + 0.5, i + 0.5, FIELD - i * 2 - 1, FIELD - i * 2 - 1);
    }
    ctx.globalAlpha = 1;
  }

  // ------------------------------------------------------------- frame

  function draw(state) {
    const {
      tanks, bullets, myId, time, baseAlive,
      bonus = [], minas = [], torres = [], pa = 0, freeze = 0, baseHp = 1, baseHpMax = 1,
    } = state;

    ctx.fillStyle = '#07090c';
    ctx.fillRect(0, 0, FIELD, FIELD);

    ctx.save();
    if (shake > 0.2) {
      ctx.translate(
        Math.round((Math.random() - 0.5) * shake),
        Math.round((Math.random() - 0.5) * shake)
      );
    }

    ctx.drawImage(layer, 0, 0);
    drawWater(time);
    drawPa(pa, time);
    for (const cel of minas) drawMina(cel, time);
    drawBase(baseAlive, time, baseHp, baseHpMax);
    for (const b of bonus) drawBonus(b, time);

    for (const b of bullets) drawBullet(b);
    for (const t of tanks) if (t.alive) drawTank(t, time);
    for (const s of torres) if (s.h > 0) drawTorre(s, time);

    stepParticles();
    drawParticles();

    drawTrees(time);
    drawFreeze(freeze, time);

    const me = tanks.find((t) => t.id === myId);
    if (me && me.alive) drawMarker(me, time);

    ctx.restore();
    drawVinheta();
  }

  return { setTiles, patchTiles, spawnFx, draw, reset, get tiles() { return tiles; } };
}

// -------------------------------------------------------------- prévia da base
//
// O Modo Construção mostra a águia com o que já foi comprado. O muro tem o
// mesmo formato nos 5 mapas (colunas 10..15, linhas 23..25), então dá pra
// desenhar sem precisar do mapa da fase que ainda nem começou.
const MURO_PREVIA = [];
for (let cx = 10; cx <= 15; cx++) MURO_PREVIA.push([cx, 23]);
for (const cy of [24, 25]) for (const cx of [10, 11, 14, 15]) MURO_PREVIA.push([cx, cy]);

export function createBasePreview(canvas) {
  canvas.width = 64;
  canvas.height = 48;
  const c = canvas.getContext('2d');
  c.imageSmoothingEnabled = false;

  // recorte: colunas 9..16, linhas 20..25 → 64x48 unidades, com a águia
  // encostada embaixo, exatamente como ela fica no campo
  const OX = 9 * TILE, OY = 20 * TILE;

  function draw(forts = {}, time = 0) {
    c.fillStyle = '#101319';
    c.fillRect(0, 0, 64, 48);

    const nAco = Math.min(3, forts.aco | 0);
    // mesma ordem do jogo: as células mais perto da águia viram aço primeiro
    const centro = [12.5, 24.5];
    const ordem = [...MURO_PREVIA].sort((a, b) =>
      (Math.abs(a[0] - centro[0]) + Math.abs(a[1] - centro[1])) -
      (Math.abs(b[0] - centro[0]) + Math.abs(b[1] - centro[1])));
    const deAco = new Set(ordem.slice(0, Math.round((ordem.length * nAco) / 3)).map((p) => p.join(',')));

    for (const [cx, cy] of MURO_PREVIA) {
      const x = cx * TILE - OX, y = cy * TILE - OY;
      if (deAco.has(`${cx},${cy}`)) {
        c.fillStyle = '#aeb6c2'; c.fillRect(x, y, TILE, TILE);
        c.fillStyle = '#dde3ea'; c.fillRect(x, y, TILE - 1, 1);
        c.fillStyle = '#7d8590'; c.fillRect(x + 2, y + 2, 4, 4);
      } else {
        c.fillStyle = '#4a1c0b'; c.fillRect(x, y, TILE, TILE);
        c.fillStyle = '#c85f1c'; c.fillRect(x, y, TILE, 3);
        c.fillRect(x, y + 4, TILE, 3);
        c.fillStyle = '#e88a3f'; c.fillRect(x, y, TILE, 1);
      }
    }

    // águia
    const bx = 12 * TILE - OX, by = 24 * TILE - OY;
    c.save();
    c.translate(bx, by);
    c.fillStyle = '#f0f2f5';
    c.fillRect(7, 1, 2, 2); c.fillRect(6, 3, 4, 8);
    c.fillRect(1, 4, 3, 3); c.fillRect(12, 4, 3, 3);
    c.fillRect(3, 6, 10, 2); c.fillRect(4, 11, 8, 1);
    c.fillStyle = '#e8a020'; c.fillRect(9, 2, 2, 1);
    c.fillStyle = '#c9a227'; c.fillRect(1, 12, 14, 2);
    c.restore();

    // blindagem: um anel verde por tiro aguentado
    const nBlind = forts.blindagem | 0;
    for (let i = 0; i < nBlind; i++) {
      c.globalAlpha = 0.75;
      c.strokeStyle = '#8ce07a';
      c.lineWidth = 1;
      c.strokeRect(bx - 1.5 - i * 2, by - 1.5 - i * 2, TANK + 3 + i * 4, TANK + 3 + i * 4);
    }
    c.globalAlpha = 1;

    // minas espalhadas no terreno em volta, que é onde elas nascem de verdade
    const nMinas = (forts.minas | 0) * 2;
    for (let i = 0; i < nMinas; i++) {
      const px = 6 + (i % 3) * 22;
      const py = 8 + Math.floor(i / 3) * 9;
      c.fillStyle = '#3b414b'; c.fillRect(px, py, 6, 4);
      c.fillStyle = '#e5484d'; c.fillRect(px + 2, py - 1, 2, 1);
    }
    // sentinelas nos flancos
    const nTorres = forts.sentinela | 0;
    for (let i = 0; i < nTorres; i++) {
      const px = i === 0 ? 1 : 49;
      c.fillStyle = '#4a5361'; c.fillRect(px, 20, 14, 14);
      c.fillStyle = '#2b3037'; c.fillRect(px + 3, 23, 8, 8);
      c.fillStyle = '#8ce07a'; c.fillRect(px + 5, 25, 4, 4);
    }

    // auto-reparo: chavezinha girando no canto
    if (forts.reparo) {
      c.fillStyle = '#8ce07a';
      const passo = Math.floor(time / 220) % 4;
      c.fillRect(54 + (passo % 2), 2 + Math.floor(passo / 2), 4, 4);
      c.fillRect(56, 6, 2, 4);
    }
  }

  return { draw };
}

// Clareia (f > 0) ou escurece (f < 0) uma cor #rrggbb.
function shade(hex, f) {
  const n = parseInt(hex.slice(1), 16);
  const ch = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((v) =>
    Math.max(0, Math.min(255, Math.round(f < 0 ? v * (1 + f) : v + (255 - v) * f)))
  );
  return `rgb(${ch[0]},${ch[1]},${ch[2]})`;
}
