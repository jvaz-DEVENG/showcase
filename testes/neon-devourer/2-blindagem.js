/* Cada teste aqui reproduz um defeito REAL encontrado na auditoria de rede.
   Contexto: o canal do Supabase Realtime é aberto — todo cliente do canal
   recebe E envia tudo. Nada que chega pela rede pode ser confiado. */
const { novaInstancia, novoPlacar } = require('./harness');
const P = novoPlacar('BLINDAGEM');

// host offline, com um convidado já no elenco
function hostComConvidado(){
  const A = novaInstancia();
  A.X.save.charLevel = 10;
  A.X.RT.papel = 'host'; A.X.RT.uid = 'HOST1'; A.X.RT.codigo = 'AAAA';
  A.X.RT.enviar = () => {};
  A.X.novoJogo('campanha'); A.quadros(5);
  A.X.receberDaRede('ola', { u:'G1', n:'CONV', s:'plasma', c:0, nv:5 });
  return A;
}

P.secao('1) nome da rede ia cru pro innerHTML (XSS remoto)');
{
  const A = hostComConvidado();
  A.X.receberDaRede('ola', { u:'G2', n:'<img src=x onerror=alert(1)>', s:'plasma', c:0 });
  const nomes = A.X.G.players.map(p => p.nome).join(' | ');
  P.ok('nome sanitizado', !/[<>]/.test(nomes), nomes);
  A.X.renderSala();
  P.ok('nada de tag no innerHTML', !/<img|onerror/i.test(A.el('salaLista').innerHTML || ''));
  A.X.receberDaRede('ola', { u:'G3', n:'A'.repeat(5000), s:'plasma', c:0 });
  P.ok('nome gigante cortado em 10', A.X.G.players[2].nome.length <= 10,
       A.X.G.players[2].nome.length + ' chars');
}

P.secao('2) cor inválida derrubava o render do host');
for(const cor of [-1, 'x', 1e400, null, 99999, {}]){
  const A = hostComConvidado();
  let quebrou = false;
  try{
    A.X.receberDaRede('ola', { u:'GX', n:'MAU', s:'orbe', c:cor });
    const p = A.X.G.players[2];
    if(!p || !p.par || p.par[0] === undefined) quebrou = true;
    A.X.renderSala();
  }catch(e){ quebrou = true; }
  P.ok('cor ' + JSON.stringify(cor) + ' não derruba', !quebrou);
}

P.secao('3) direção não-unitária pulava parede e dobrava a velocidade');
{
  const A = hostComConvidado();
  const p = A.X.G.players[1];
  A.X.aplicarEntrada({ u:'G1', d:[2,0], a:[] });
  P.ok('limitada a 1 tile', Math.abs(p.quer.x) <= 1 && Math.abs(p.quer.y) <= 1);
  A.X.aplicarEntrada({ u:'G1', d:[1,1], a:[] });
  P.ok('diagonal recusada', !(p.quer.x && p.quer.y));
  let quebrou = false;
  for(const mau of [{u:'G1'}, {u:'G1', d:'xx', a:5}, {u:'G1', d:[null,null]}, {u:'G1', d:[]}]){
    try{ A.X.aplicarEntrada(mau); }catch(e){ quebrou = true; }
  }
  P.ok('payload malformado não estoura', !quebrou);
}

P.secao('4) atributos absurdos vindos da rede');
{
  const A = hostComConvidado();
  const p = A.X.G.players[1];
  A.X.aplicarAtributos(p, { vl:1e6, pr:1e6, pc:-999, nv:999, es:50, dc:-5 });
  P.ok('velocidade na faixa', p.velBase <= 12, 'vel=' + p.velBase);
  P.ok('raio do pulso na faixa', p.pulsoR <= 8, 'raio=' + p.pulsoR);
  P.ok('custo do pulso positivo', p.pulsoCusto >= 10, 'custo=' + p.pulsoCusto);
  P.ok('nível na faixa', p.nivel <= 99, 'nivel=' + p.nivel);
  A.X.aplicarAtributos(p, { vl:NaN, pr:null, pc:'abc' });
  P.ok('NaN não contamina', Number.isFinite(p.velBase) && Number.isFinite(p.pulsoR));
}

P.secao('5) convidado sem vidas ressuscitava a cada pacote');
{
  const A = hostComConvidado();
  const p = A.X.G.players[1];
  p.vidas = 0; p.fora = true;
  A.X.aplicarEntrada({ u:'G1', d:[1,0], a:[] });
  P.ok('continua fora com 0 vidas', p.fora === true);
  A.X.G.players[0].fora = true;
  P.ok('todos fora entao o fim de jogo dispara', A.X.G.players.every(x => x.fora));
  p.fora = false; p.vidas = 2; p.ausente = true;
  A.X.aplicarEntrada({ u:'G1', d:[1,0], a:[] });
  P.ok('quem so sumiu volta a jogar', !p.ausente && !p.fora);
}

P.secao('6) comando forjado por outro cliente do canal');
{
  const B = novaInstancia();
  B.X.RT.papel = 'guest'; B.X.RT.uid = 'EU'; B.X.RT.enviar = () => {};
  B.X.novoJogo('campanha'); B.quadros(5);
  B.X.receberDaRede('time', { h:'HOSTREAL', mo:'coop', js:[
    { u:'HOSTREAL', n:'HST', s:'plasma', c:0, v:3 },
    { u:'EU', n:'EU', s:'plasma', c:0, v:3 }] });
  P.ok('host identificado no 1o time', B.X.RT.hostUid === 'HOSTREAL');
  const antes = B.X.G.estado;
  B.X.receberDaRede('cheio',  { u:'EU', h:'IMPOSTOR' });
  P.ok('ignora cheio de impostor', B.X.G.estado === antes);
  B.X.receberDaRede('fim',    { h:'IMPOSTOR' });
  P.ok('ignora fim de impostor', B.X.RT.papel === 'guest');
  B.X.receberDaRede('acabou', { h:'IMPOSTOR', ps:[999,999], f:9 });
  P.ok('ignora acabou de impostor', B.X.RT.papel === 'guest');
}

P.secao('7) mapa truncado virava labirinto todo de parede');
{
  const B = novaInstancia();
  B.X.RT.papel = 'guest'; B.X.RT.uid = 'EU';
  P.ok('mapa curto recusado',
       B.X.aplicarMapa({co:25,ro:21,g:'000',pl:'000',f:1,b:'circuito'}) === false);
  P.ok('sem strings recusado', B.X.aplicarMapa({co:25,ro:21,f:1,b:'circuito'}) === false);
  P.ok('grade invalida recusada', B.X.aplicarMapa({co:0,ro:0,g:'',pl:'',f:1}) === false);
}

P.secao('8) mapa por broadcast fazia TODOS renascerem');
{
  const B = novaInstancia();
  B.X.RT.papel = 'guest'; B.X.RT.uid = 'EU'; B.X.RT.hostUid = 'H'; B.X.RT.enviar = () => {};
  B.X.novoJogo('campanha'); B.quadros(5);
  const eu = B.X.euJogador(); eu.carga = 88;
  B.X.receberDaRede('mapa', { h:'H', para:'OUTRO', co:25, ro:21,
    g:'0'.repeat(525), pl:'0'.repeat(525), f:2, b:'circuito', sx:1, sy:1 });
  P.ok('ignorou mapa endereçado a outro', eu.carga === 88);
}

P.secao('9) pacote de estado atrasado rebobinava posição e placar');
{
  const B = novaInstancia();
  B.X.RT.papel = 'guest'; B.X.RT.uid = 'EU'; B.X.RT.hostUid = 'H'; B.X.RT.enviar = () => {};
  B.X.novoJogo('campanha'); B.quadros(5);
  const est = n => ({ h:'H', n, ps:[[100,100,0,0,n*10,3,0,0,0,0]], ms:[], dv:0, re:5, co:[], fa:1 });
  B.X.receberDaRede('estado', est(10));
  const depois = B.X.G.players[0].score;
  B.X.receberDaRede('estado', est(4));
  P.ok('pacote fora de ordem ignorado', B.X.G.players[0].score === depois, 'score=' + depois);
}

P.secao('10) slot fantasma travava a sala em "cheia"');
{
  const A = hostComConvidado();
  A.X.G.estado = 'sala'; A.X.RT.enviar = () => {};
  A.X.RT.pares[0].visto = Date.now()/1000 - 100;
  A.X.expirarAusentes(Date.now()/1000);
  P.ok('lugar liberado depois de 45s',
       A.X.G.players.length === 1 && A.X.RT.pares.length === 0,
       A.X.G.players.length + ' jogador(es)');
}

P.secao('11) host caido: extrapolacao sem limite atravessava o mapa');
{
  const B = novaInstancia();
  B.X.RT.papel = 'guest'; B.X.RT.uid = 'EU'; B.X.RT.hostUid = 'H'; B.X.RT.enviar = () => {};
  B.X.novoJogo('campanha'); B.quadros(5);
  B.X.G.players.push(B.X.criarPlayer(1, { nome:'OUTRO', local:false }));
  const o = B.X.G.players[1];
  o.alvoX = o.x; o.alvoY = o.y; o.dir = B.X.DIRS.right;
  const x0 = o.alvoX;
  B.X.RT.semEstado = 5;
  for(let i = 0; i < 300; i++) B.X.atualizarConvidado(1/60);
  const andou = Math.abs(o.alvoX - x0) / B.X.TILE;
  P.ok('congela sem pacote em vez de derivar', andou < 1, andou.toFixed(2) + ' tiles em 5s');
}

P.secao('12) colisao e mira ignoravam o tunel');
{
  const A = novaInstancia();
  A.X.novoJogo('campanha'); A.quadros(5);
  const W = A.X.W();
  P.ok('encostados pelo tunel = 1 tile',
       Math.abs(A.X.distWrap(11, 100, W - 11, 100) - 22) < 1,
       A.X.distWrap(11, 100, W - 11, 100).toFixed(1) + 'px');
  P.ok('distancia normal inalterada',
       Math.abs(A.X.distWrap(11, 100, 200, 100) - 189) < 1);
}

(async () => {
  P.secao('13) rede parava na tela de talentos (relogio de parede: espera real)');
  const A = hostComConvidado();
  A.X.RT.entrou = true; A.X.RT.aoVivo = true;
  const saidas = [];
  A.X.RT.enviar = ev => saidas.push(ev);
  A.X.G.estado = 'upgrades';
  const ate = Date.now() + 1300;
  while(Date.now() < ate){ A.X.tickRede(1/60); await new Promise(r => setTimeout(r, 20)); }
  P.ok('host segue dando sinal de vida', saidas.includes('vivo'), saidas.join(',') || '(nada)');

  P.secao('14) convidado podia mandar 1 msg por quadro (180/s de fan-out)');
  const B = novaInstancia();
  B.X.RT.papel = 'guest'; B.X.RT.uid = 'EU'; B.X.RT.hostUid = 'H'; B.X.RT.entrou = true;
  B.X.novoJogo('campanha'); B.quadros(5);
  let msgs = 0;
  B.X.RT.enviar = () => msgs++;
  B.X.G.estado = 'jogando';
  const dirs = [B.X.DIRS.left, B.X.DIRS.right, B.X.DIRS.up, B.X.DIRS.down];
  const fim = Date.now() + 1000;
  let i = 0;
  while(Date.now() < fim){
    B.X.euJogador().quer = dirs[(i++) % 4];
    B.X.tickRede(1/60);
    await new Promise(r => setTimeout(r, 4));
  }
  P.ok('teto de 10 msg/s respeitado', msgs <= 13, msgs + ' msg em 1s (antes: ate 60)');
  P.fim();
})();
