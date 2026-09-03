// Suite do Cosmic Crush.
// Rodar: node .claude/skills/run-github-showcase/tests/test-cosmic-crush.js
const H = require('./harness.js');
const { carregar, criarSuite, setBoard, limparBlocos, settle, integro, TAB } = H;
const S = criarSuite();
const ok = S.ok;
let { A } = carregar();

S.secao('1. tabuleiro e deteccao');
A.startLevel(1);
ok(!A.temCombinacao(), 'fase abre sem combinacao pronta');
ok(A.hasMove(), 'fase abre com jogada possivel');
ok(!integro(A), 'tabuleiro integro', integro(A));

limparBlocos(A);
setBoard(A, ['11123456','23451234','34512345','45123451','51234512','12345123','23451234','34512345']);
ok(A.findRuns().length === 1, 'acha fileira de 3');

S.secao('2. grupos conectados de formato livre');
limparBlocos(A);
setBoard(A, ['11234501','11345012','23450123','34501234','45012345','50123450','01234501','12345012']);
ok(!!A.findGroups().find(g => g.type === 1 && g.cells.length === 4), 'quadrado 2x2 combina');
ok(A.planClear(null).specials.length === 0, '4 pecas tortas nao geram especial');

limparBlocos(A);
setBoard(A, TAB);
A.board[3][1].t = 1; A.board[3][2].t = 1; A.board[3][3].t = 1; A.board[2][2].t = 1;
const g3 = A.findGroups().find(g => g.type === 1 && g.set.has(A.K(3,2)));
ok(g3 && g3.cells.length >= 4 && g3.set.has(A.K(2,2)), 'fileira de 3 leva a peca grudada junto');

limparBlocos(A);
setBoard(A, TAB);
A.board[5][1].t = 3; A.board[5][2].t = 3; A.board[4][1].t = 3;
ok(!A.findGroups().some(g => g.type === 3 && g.cells.length === 3), 'L de so 3 pecas nao combina');

S.secao('3. pecas especiais');
const planFor = (linhas, swap) => { limparBlocos(A); setBoard(A, linhas); return A.planClear(swap || null); };
ok(planFor(['11112345','23451234','34512345','45123451','51234512','12345123','23451234','34512345']).specials[0].kind === 'row', '4 na horizontal cria foguete');
ok(planFor(['11111345','23452234','34513345','45124451','51235512','12341123','23452234','34513345']).specials[0].kind === 'hole', '5 na horizontal cria buraco negro');
ok(planFor(['11123456','13451234','13512345','45123451','51234512','12345123','23451234','34512345']).specials[0].kind === 'nova', 'formato em L cria supernova');
ok(planFor(['11112345','23451234','34512345','45123451','51234512','12345123','23451234','34512345'], [[0,3],[1,3]]).specials[0].c === 3, 'especial nasce na peca movida');

S.secao('4. combinacoes entre especiais');
function combo(sa, sb) {
  A.startLevel(1); limparBlocos(A); setBoard(A, TAB);
  const a = A.board[4][3], b = A.board[4][4];
  a.special = sa; b.special = sb;
  if (sa === 'hole') { a.t = -1; a.holeTarget = null; }
  if (sb === 'hole') { b.t = -1; b.holeTarget = null; }
  return A.comboClear(a, b);
}
ok(combo('row','row').size === 15, 'foguete + foguete vira cruz');
ok(combo('row','nova').size >= 30, 'foguete + supernova varre 3 linhas e 3 colunas');
const dd = combo('nova','nova').size;
ok(dd >= 20 && dd <= 30, 'supernova + supernova explode 5x5', String(dd));
ok(combo('hole','hole').size === 64, 'dois buracos negros limpam o tabuleiro');

S.secao('5. gravidade com bloqueios');
A.startLevel(1); limparBlocos(A); setBoard(A, TAB);
A.blocks[4][2] = { kind:'ice', hp:1, max:1 };
A.board[4][2] = null; A.board[6][2] = null; A.board[2][2] = null;
A.applyGravity();
ok(!integro(A), 'gravidade segmentada nao fura nem duplica', integro(A));
ok(A.board[4][2] === null, 'celula bloqueada continua sem peca');

S.secao('6. objetivos por tipo de fase');
A.startLevel(3); ok(A.cfg.kind === 'ice' && A.iceLeft > 0, 'fase de gelo abre com gelo');
A.iceLeft = 0; ok(A.goalsMet(), 'zerar o gelo cumpre o objetivo');
A.startLevel(6); A.cargoDone = A.cfg.cargo; ok(A.goalsMet(), 'resgatar as capsulas cumpre');
A.startLevel(5); A.score = A.cfg.target; ok(A.goalsMet(), 'bater a pontuacao cumpre');

S.secao('7. ignicao final');
A.startLevel(5);
A.phase = 'idle'; A.score = A.cfg.target + 1;
A.finishTurn();
ok(A.phase === 'finale', 'objetivo cumprido com movimento sobrando entra na ignicao');
ok(settle(A) < 9000, 'a ignicao termina');
ok(A.phase === 'over' && A.movesLeft === 0, 'gasta todos os movimentos e encerra');

S.secao('8. REGRESSAO dos bugs corrigidos');

let travadas = 0;
for (let i = 0; i < 400; i++) {
  A.startLevel([6, 9, 13][i % 3]);
  if (!A.hasMove()) travadas++;
}
ok(travadas === 0, 'bug1: fase de resgate nunca abre travada', travadas + '/400 travadas');

A.startLevel(6); limparBlocos(A); setBoard(A, TAB);
A.board[3][3] = A.makeCargo(3, 3);
A.phase = 'idle'; A.save.boosters.hammer = 2;
A.armed = 'hammer'; A.applyBooster({ r:3, c:3 });
ok(A.save.boosters.hammer === 2, 'bug2: martelo na capsula nao consome estoque', 'ficou ' + A.save.boosters.hammer);
ok(A.armed === 'hammer', 'bug2: e o poder segue armado para outro alvo');

const corrompidos = [
  ['null', 'null'],
  ['[]', 'array'],
  ['"abc"', 'texto'],
  ['{"boosters":{"hammer":"x"}}', 'booster texto'],
  ['{"unlocked":-5,"cristais":"NaN"}', 'numeros invalidos']
];
corrompidos.forEach(function (par) {
  const bruto = par[0], rotulo = par[1];
  let quebrou = null, api = null;
  try { api = carregar(bruto).A; } catch (e) { quebrou = e.message; }
  ok(!quebrou, 'bug3: save ' + rotulo + ' nao derruba o boot', quebrou);
  if (api) {
    const b = api.save.boosters;
    ok(Object.keys(b).every(k => Number.isInteger(b[k]) && b[k] >= 0), 'bug3: save ' + rotulo + ' da estoque numerico', JSON.stringify(b));
    ok(api.save.unlocked >= 1 && Number.isInteger(api.save.cristais), 'bug3: save ' + rotulo + ' da progresso valido');
  }
});
const apiInf = carregar('{"boosters":{"hammer":"x"}}').A;
ok(apiInf.save.boosters.hammer >= 0 && apiInf.save.boosters.hammer < 99, 'bug3: booster corrompido nao vira poder infinito', String(apiInf.save.boosters.hammer));

A.startLevel(6); limparBlocos(A); setBoard(A, TAB);
A.board[3][3] = A.makeCargo(3, 3);
A.cascade = 1;
A.startPop(new Set([A.K(3,2), A.K(3,4)]), [{ r:4, c:3, type:1, kind:'nova' }]);
ok(!A.popList.some(t => t.r === 4 && t.c === 3), 'bug4: especial nascendo nao entra no estouro');
ok(A.board[4][3] && A.board[4][3].special === 'nova', 'bug4: e sobrevive no tabuleiro');
settle(A);

A.startLevel(8); limparBlocos(A); setBoard(A, TAB);
const ha = A.board[4][3], hb = A.board[4][4];
const corAlvo = hb.t;
for (let r = 0; r < 8; r++) for (let c = 0; c < 8; c++) if (A.board[r][c].t === corAlvo) A.board[r][c].locked = true;
hb.locked = false;
ha.special = 'hole'; ha.t = -1; hb.special = 'row';
A.comboClear(ha, hb);
let mortos = 0;
for (let r = 0; r < 8; r++) for (let c = 0; c < 8; c++) {
  const t = A.board[r][c];
  if (t && t.locked && t.special) mortos++;
}
ok(mortos === 0, 'bug5: chuva de foguetes nao cria foguete morto em peca acorrentada', mortos + ' mortos');
settle(A);

const semDark = [];
for (let lv = 1; lv <= 200; lv++) {
  const c = A.levelConfig(lv);
  if (!c.dark) continue;
  A.startLevel(lv);
  let n = 0;
  for (let r = 0; r < 8; r++) for (let cc = 0; cc < 8; cc++) if (A.blocks[r][cc] && A.blocks[r][cc].kind === 'dark') n++;
  if (n < c.dark) semDark.push(lv + ' (pediu ' + c.dark + ', veio ' + n + ')');
}
ok(semDark.length === 0, 'bug6: toda fase que pede materia escura recebe', semDark.slice(0, 5).join(', '));

A.startLevel(1); limparBlocos(A); setBoard(A, TAB);
for (let r = 0; r < 8; r++) for (let c = 0; c < 8; c++) {
  if (r === 4 && c === 4) continue;
  A.board[r][c] = null;
  A.blocks[r][c] = { kind:'ice', hp:9, max:9 };
}
A.board[4][4].special = 'hole'; A.board[4][4].t = -1;
ok(A.hasMove() === false, 'bug7: buraco negro sem vizinho nao conta como jogada');

A.startLevel(8); limparBlocos(A); setBoard(A, TAB);
A.score = 0; A.cascade = 1;
A.board[2][2].locked = true; A.board[2][3].locked = true; A.board[2][4].locked = true;
A.startPop(new Set([A.K(2,2), A.K(2,3), A.K(2,4)]), []);
ok(A.score > 0, 'bug8: estouro que so quebra corrente soma pontos', 'score ' + A.score);
settle(A);

A.startLevel(1); limparBlocos(A);
setBoard(A, ['11112345','23451234','34512345','45123451','51234512','12345123','23451234','34512345']);
A.board[0][3].special = 'nova';
const plano9 = A.planClear([[0,3],[1,3]]);
ok(!plano9.specials.some(s => s.r === 0 && s.c === 3), 'bug9: especial nova nao nasce em cima de outra', JSON.stringify(plano9.specials));
ok(plano9.clear.has(A.K(0,3)), 'bug9: a especial antiga fica no estouro para disparar');

S.secao('9. economia, poderes e telas');
A.startLevel(2); A.save.cristais = 1000;
const estH = A.save.boosters.hammer;
A.comprar('hammer', 1, A.PRECOS.hammer);
ok(A.save.boosters.hammer === estH + 1 && A.save.cristais === 1000 - A.PRECOS.hammer, 'comprar debita e entrega');
A.save.cristais = 10;
const estB = A.save.boosters.bomb;
A.comprar('bomb', 1, A.PRECOS.bomb);
ok(A.save.boosters.bomb === estB && A.save.cristais === 10, 'sem saldo nao compra');

A.startLevel(3); A.save.cristais = 0; A.score = 999999; A.endLevel(true);
ok(A.save.cristais >= 100, 'concluir fase rende cristais');
ok(A.save.best[3] !== undefined, 'guarda recorde da fase');

let quebrouTela = null;
try {
  A.showMenu(); A.showShop(); A.showPerfil(); A.showConquistas(); A.showTutorial(); A.showMap();
  A.startLevel(2); A.endLevel(false);
} catch (e) { quebrouTela = e.message; }
ok(!quebrouTela, 'todas as telas montam sem erro', quebrouTela);

S.secao('10. partidas automaticas (30 fases)');
let erros = 0, jogadas = 0;
const tipos = {};
for (let lv = 1; lv <= 30; lv++) {
  A.startLevel(lv);
  tipos[A.cfg.kind] = 1;
  let seg = 0;
  while (A.phase === 'idle' && seg++ < 400) {
    const h = A.findHint();
    if (!h) { A.garantirJogada(); if (!A.findHint()) break; continue; }
    const a = A.board[h[0][0]][h[0][1]], b = A.board[h[1][0]][h[1][1]];
    if (!a || !b) break;
    A.tryMove(a, b); jogadas++;
    if (settle(A) >= 9000) { erros++; break; }
    const bad = integro(A);
    if (bad) { erros++; console.log('  integridade fase ' + lv + ': ' + bad); break; }
    if (A.phase === 'over') break;
  }
}
ok(erros === 0, 'nenhuma falha em 30 fases jogadas ate o fim', 'erros: ' + erros);
ok(Object.keys(tipos).length === 4, 'as 30 primeiras cobrem os 4 tipos');
console.log('  (' + jogadas + ' jogadas simuladas)');

S.secao('11. config das fases');
for (let lv = 1; lv <= 120; lv++) {
  const c = A.levelConfig(lv);
  ok(c.moves > 0 && c.stars[0] < c.stars[1] && c.stars[1] < c.stars[2], 'fase ' + lv + ' com config valida');
}
ok(JSON.stringify(A.levelConfig(40)) === JSON.stringify(A.levelConfig(40)), 'fase gerada e deterministica');

S.secao('12. teto da curva procedural');
// a fase 15 (a mais dura feita a mao) pede 4.1 pecas por movimento.
// nenhuma fase gerada pode passar disso com folga, senao vira impossivel.
let piorRatio = 0, piorFase = 0, piorCargo = 0, piorScore = 0;
for (let lv = 16; lv <= 400; lv++) {
  const c = A.levelConfig(lv);
  if (c.kind === 'collect') {
    const total = c.goals.reduce((s, g) => s + g[1], 0);
    const ratio = total / c.moves;
    if (ratio > piorRatio) { piorRatio = ratio; piorFase = lv; }
  } else if (c.kind === 'cargo') {
    piorCargo = Math.max(piorCargo, c.cargo);
  } else if (c.kind === 'score') {
    piorScore = Math.max(piorScore, c.target / c.moves);
  }
}
ok(piorRatio <= 4.6, 'coleta nunca passa de 4.6 pecas por movimento',
   'pior: ' + piorRatio.toFixed(2) + ' na fase ' + piorFase);
ok(piorCargo <= 5, 'resgate nunca pede mais de 5 capsulas', 'pior: ' + piorCargo);
ok(piorScore <= 900, 'pontuacao nunca passa de 900 por movimento', 'pior: ' + Math.round(piorScore));

process.exit(S.resumo() ? 1 : 0);
