/* Sobe o jogo dentro de um contexto Node isolado, com um DOM/canvas de mentira.
   Serve para testar a lógica sem navegador. O que NÃO dá pra testar aqui:
   pixel na tela, FPS real e toque — isso é Chrome DevTools.

   Armadilha registrada: o index.html tem DOIS <script> (modo vitrine + jogo).
   Regex gulosa pegava do primeiro <script> até o último </script> e engolia a
   tag do meio, quebrando com erro de sintaxe. Pegamos sempre o MAIOR bloco. */
const fs = require('fs');
const vm = require('vm');
const path = require('path');

const JOGO = process.env.NEON_HTML ||
  path.join(__dirname, '..', '..', 'minijogos', 'neon-devourer', 'index.html');

function corpoDoJogo(){
  const html = fs.readFileSync(JOGO, 'utf8');
  const blocos = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
  if(!blocos.length) throw new Error('nenhum <script> em ' + JOGO);
  return blocos.sort((a, b) => b.length - a.length)[0].replace('"use strict";', '');
}

const noop = () => {};

function novaInstancia(opt = {}){
  const { retrato = false, comRede = false, uid = null } = opt;
  const ctx2d = () => new Proxy({}, { get: (t, k) =>
      k === 'measureText' ? () => ({ width: 10 }) :
      (k === 'createRadialGradient' || k === 'createLinearGradient')
        ? () => ({ addColorStop: noop }) : noop,
    set: () => true });

  function elemento(tag){
    const e = {
      tag, width: 0, height: 0, offsetHeight: 40, offsetParent: {},
      style: { setProperty: noop, display: '' }, dataset: {}, _filhos: [], _ev: {},
      textContent: '', className: '', value: '', disabled: false,
      classList: {
        _s: new Set(),
        add(x){ this._s.add(x); }, remove(x){ this._s.delete(x); },
        toggle(x, v){ v ? this._s.add(x) : this._s.delete(x); },
        contains(x){ return this._s.has(x); }
      },
      appendChild(c){ this._filhos.push(c); return c; },
      _attrs: {},
      setAttribute(k, v){ this._attrs[k] = String(v); },
      getAttribute(k){ return k in this._attrs ? this._attrs[k] : null; },
      removeAttribute(k){ delete this._attrs[k]; },
      hasAttribute(k){ return k in this._attrs; },
      querySelector: () => elemento('b'),
      querySelectorAll: () => [],
      addEventListener(t, f){ (this._ev[t] || (this._ev[t] = [])).push(f); },
      focus: noop, select: noop, click(){ (this._ev.click || []).forEach(f => f({
        preventDefault: noop, stopPropagation: noop })); },
      getContext: () => ctx2d(),
      get children(){ return this._filhos; }
    };
    Object.defineProperty(e, 'innerHTML', {
      get(){ return this._html || ''; },
      set(v){ this._html = v; this._filhos = []; }
    });
    return e;
  }

  const elementos = {};
  const teclado = [];
  const enviados = [];

  const ctx = {
    document: {
      getElementById: id => elementos[id] || (elementos[id] = elemento('div')),
      createElement: t => elemento(t),
      addEventListener: noop,
      querySelector: () => null,
      querySelectorAll: () => [],
      get activeElement(){ return null; },
      body: elemento('body')
    },
    window: {
      addEventListener: (t, f) => { if(t === 'keydown') teclado.push(f); },
      innerWidth:  retrato ? 390 : 1440,
      innerHeight: retrato ? 844 : 900
    },
    location: { search: '', href: 'http://teste/' },
    matchMedia: () => ({ matches: false }),
    navigator: { hardwareConcurrency: 16 },
    screen: { orientation: { lock: () => Promise.resolve(), unlock: noop } },
    localStorage: { getItem: () => null, setItem: noop },
    Storage: function(){},
    performance: { now: () => 0 },
    WebSocket: comRede ? global.WebSocket
      : function(){ this.readyState = 1; this.send = m => enviados.push(m); this.close = noop; },
    fetch: comRede ? global.fetch : () => Promise.reject(new Error('rede desligada no teste')),
    setTimeout, clearTimeout, setInterval, clearInterval,
    console: { log: noop, warn: noop, error: noop },
    Math, JSON, Date, Object, Array, String, Number, Boolean, Set, Map, Promise,
    isNaN, parseInt, parseFloat, encodeURIComponent, decodeURIComponent,
    requestAnimationFrame: cb => { ctx.__raf = cb; },
    __raf: null, __elementos: elementos, __teclado: teclado, __enviados: enviados
  };
  ctx.Storage.prototype = { setItem: noop };
  ctx.globalThis = ctx;
  ctx.self = ctx;
  vm.createContext(ctx);

  // exporta tudo que os testes usam; COLS/ROWS mudam, então vão por função
  vm.runInContext(corpoDoJogo() + `
    ;globalThis.X = {
      G, RT, DIRS, save, TILE, MOBS, BIOMAS, UPGRADES, BUFFS, ESCALAS, SKINS,
      CUSTO_FASE, attr, temPulso, temFase, nivelDe, vidasDe, agressao,
      novoJogo, montarFase, proximaFase, concluirFase, fimDeJogo, montarTime,
      euJogador, criarPlayer, criarMob, renascer, perderVida, devorarMob,
      tileDe, livre, centro, distWrap, campoColeta, coletar, maisProximo,
      usarDash, usarPulso, usarFase, pulsoOnda, miraLivre, atirar,
      tiposDaFase, atualizarMobs, atualizarHud, atualizarConvidado,
      mostrar, esconderTudo, pausar, retomar, renderSala, renderPausa,
      limparNome, nomeDoJogador, placarFinal, faseDeRetomada, atualizarContinuar,
      tickRede, tickMusica, expirarAusentes, reavaliarGrade,
      criarSala, entrarNaSala, comecarOnline, sairDaSala,
      receberDaRede, aplicarEntrada, aplicarAtributos, aplicarMapa, aplicarEstado,
      serializarMapa, montarEstado, enviarTime, gerarCodigo,
      grade: () => [COLS, ROWS], W: () => W, H: () => H
    };`, ctx);

  let relogio = 0;
  // um quadro = uma chamada do requestAnimationFrame que o jogo agendou
  ctx.quadros = n => {
    for(let i = 0; i < n; i++){
      if(!ctx.__raf) break;
      const cb = ctx.__raf; ctx.__raf = null;
      relogio += 16.7; cb(relogio);
    }
  };
  ctx.tecla = code => {
    const ev = { code, preventDefault: noop, stopPropagation: noop };
    teclado.forEach(f => f(ev));
  };
  ctx.el = id => elementos[id];
  return ctx;
}

// --------- placar de asserções, igual em todas as suítes ---------
function novoPlacar(titulo){
  const falhas = [];
  return {
    secao(t){ console.log('\n=== ' + t + ' ==='); },
    ok(desc, cond, extra = ''){
      console.log(`  ${cond ? 'OK  ' : 'FALHA'} ${desc}${extra ? ' — ' + extra : ''}`);
      if(!cond) falhas.push(desc);
    },
    info(t){ console.log('  ' + t); },
    fim(){
      console.log('\n' + (falhas.length ? `${falhas.length} FALHA(S) em ${titulo}` : titulo + ' OK'));
      falhas.forEach(f => console.log(' - ' + f));
      process.exitCode = falhas.length ? 1 : 0;
      return falhas.length;
    }
  };
}

const esperar = ms => new Promise(r => setTimeout(r, ms));

module.exports = { novaInstancia, novoPlacar, esperar, JOGO, corpoDoJogo };
