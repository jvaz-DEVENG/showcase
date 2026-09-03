// Harness headless do Cosmic Crush: stuba o DOM e injeta hooks dentro do IIFE.
// Mora no repositorio de proposito — a versao anterior vivia num scratchpad
// temporario e foi apagada junto com a sessao, levando a suite inteira.
const fs = require('fs');
const vm = require('vm');
const path = require('path');

const JOGO = path.resolve(__dirname, '../../../../minijogos/cosmic-crush/index.html');

const HOOK = `
globalThis.__api = {
  get board(){ return board; },      set board(v){ board = v; },
  get blocks(){ return blocks; },    set blocks(v){ blocks = v; },
  get phase(){ return phase; },      set phase(v){ phase = v; },
  get score(){ return score; },      set score(v){ score = v; },
  get movesLeft(){ return movesLeft; }, set movesLeft(v){ movesLeft = v; },
  get collected(){ return collected; },
  get cascade(){ return cascade; },  set cascade(v){ cascade = v; },
  get popList(){ return popList; },
  get armed(){ return armed; },      set armed(v){ armed = v; },
  get save(){ return save; },        set save(v){ save = v; },
  get cfg(){ return cfg; },
  get iceLeft(){ return iceLeft; },  set iceLeft(v){ iceLeft = v; },
  get cargoDone(){ return cargoDone; },       set cargoDone(v){ cargoDone = v; },
  get cargoPending(){ return cargoPending; }, set cargoPending(v){ cargoPending = v; },
  get darkPending(){ return darkPending; },   set darkPending(v){ darkPending = v; },
  get level(){ return level; },
  makeTile, makeCargo, findRuns, findGroups, temCombinacao, planClear, activateSpecials,
  comboClear, applyGravity, hasMove, findHint, buildBoard, buildBlocks, shuffleBoard,
  swapLogical, startPop, stepResolve, startLevel, levelConfig, tryMove, forceSwap,
  tickSwap, tickPop, tickFall, tickFinale, goalsMet, finishTurn, espalharDark, hitBlock,
  coletarCargo, garantirJogada, semearCargo, loadSave, persist, K, CARGO, VIZINHOS,
  useBooster, applyBooster, disarm, endLevel, showMap, showMenu, showWin, showLose,
  showShop, showPerfil, showConquistas, showTutorial, comprar, checarConquistas,
  totalEstrelas, CONQUISTAS, PRECOS, LEVELS, START_BOOSTERS
};
`;

function carregar(saveInicial) {
  // o arquivo tem mais de um <script>: pega o maior, que e o jogo
  const blocos = [...fs.readFileSync(JOGO, 'utf8').matchAll(/<script>([\s\S]*?)<\/script>/g)]
    .map(m => m[1]);
  let js = blocos.sort((a, b) => b.length - a.length)[0];
  js = js.replace(/\n\}\)\(\);\s*$/, '\n' + HOOK + '\n})();\n');
  if (!js.includes('__api')) throw new Error('falha ao injetar hooks no IIFE');

  const noop = () => {};
  const fakeCtx = () => new Proxy({}, {
    get(t, p) {
      if (p === 'canvas') return {};
      if (p === 'createRadialGradient' || p === 'createLinearGradient')
        return () => ({ addColorStop: noop });
      if (p === 'measureText') return () => ({ width: 10 });
      return typeof p === 'string' ? (t[p] !== undefined ? t[p] : noop) : noop;
    },
    set(t, p, v) { t[p] = v; return true; }
  });
  const fakeEl = tag => ({
    tagName: tag, children: [], style: {}, dataset: {}, width: 0, height: 0,
    clientWidth: 432, innerHTML: '', textContent: '', title: '', disabled: false,
    classList: { add: noop, remove: noop, toggle: noop, contains: () => false },
    getContext: fakeCtx,
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 432, height: 432 }),
    addEventListener: noop, setPointerCapture: noop, setAttribute: noop,
    appendChild(c) { this.children.push(c); this.lastChild = c; return c; }
  });

  const store = {};
  if (saveInicial !== undefined) store['cosmic-crush.v1'] = saveInicial;
  const ctx = {
    localStorage: {
      getItem: k => (k in store ? store[k] : null),
      setItem: (k, v) => { store[k] = String(v); }
    },
    document: {
      getElementById: () => fakeEl('div'),
      createElement: fakeEl,
      createElementNS: (ns, tag) => fakeEl(tag),
      addEventListener: noop
    },
    window: { addEventListener: noop, devicePixelRatio: 1 },
    requestAnimationFrame: () => 0,
    setTimeout: () => 0, clearTimeout: noop, setInterval: () => 0,
    location: { search: '' },
    // o jogo carrega um sprite de cometa; no headless basta um objeto inerte
    Image: function () { this.onload = null; this.onerror = null; this.src = ''; },
    Node: function () {},          // o jogo usa `instanceof Node` ao montar as telas
    CanvasRenderingContext2D: function () {},
    console, Math, JSON, Date, Set, Map, Array, Object, Number, String, parseInt, parseFloat
  };
  ctx.CanvasRenderingContext2D.prototype = {};
  ctx.globalThis = ctx;
  vm.createContext(ctx);
  vm.runInContext(js, ctx, { filename: 'cosmic-crush.js' });
  return { A: ctx.__api, store };
}

// ---- utilitarios de teste ----
function criarSuite() {
  let ok_ = 0, falhas = [];
  const ok = (cond, nome, extra) => {
    if (cond) ok_++;
    else falhas.push(nome + (extra ? ' — ' + extra : ''));
  };
  return {
    ok,
    secao: t => console.log('== ' + t + ' =='),
    resumo() {
      falhas.forEach(f => console.log('  FALHOU: ' + f));
      console.log('\n' + ok_ + ' verificacoes OK, ' + falhas.length + ' falhas.');
      return falhas.length;
    }
  };
}

const TAB = ['01234501','12345012','23450123','34501234','45012345','50123450','01234501','12345012'];

function setBoard(A, linhas) {
  const b = [];
  for (let r = 0; r < 8; r++) {
    b.push([]);
    for (let c = 0; c < 8; c++)
      b[r].push(linhas[r][c] === '.' ? null : A.makeTile(Number(linhas[r][c]), r, c));
  }
  A.board = b;
  return b;
}
function limparBlocos(A) {
  const b = [];
  for (let r = 0; r < 8; r++) { b.push([]); for (let c = 0; c < 8; c++) b[r].push(null); }
  A.blocks = b;
}
function settle(A, limite) {
  let g = 0;
  while (A.phase !== 'idle' && A.phase !== 'over' && g++ < (limite || 9000)) {
    if (A.phase === 'pop') A.tickPop(0.02);
    else if (A.phase === 'fall') A.tickFall(0.02);
    else if (A.phase === 'swap') A.tickSwap(0.02);
    else if (A.phase === 'finale') A.tickFinale(0.02);
  }
  return g;
}
function integro(A) {
  for (let r = 0; r < 8; r++) for (let c = 0; c < 8; c++) {
    const t = A.board[r][c], b = A.blocks[r][c];
    if (t && b) return 'peca e bloqueio juntos em ' + r + ',' + c;
    if (!t && !b) return 'buraco em ' + r + ',' + c;
    if (t && (t.r !== r || t.c !== c)) return 'coordenada errada em ' + r + ',' + c;
  }
  const ids = new Set();
  let n = 0;
  for (let r = 0; r < 8; r++) for (let c = 0; c < 8; c++) if (A.board[r][c]) { ids.add(A.board[r][c].id); n++; }
  if (ids.size !== n) return 'peca duplicada';
  return null;
}

module.exports = { carregar, criarSuite, setBoard, limparBlocos, settle, integro, TAB, JOGO };
