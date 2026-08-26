/* ONLINE DE VERDADE: sobe DUAS instâncias do jogo e faz as duas jogarem juntas
   pela internet, via Supabase Realtime. Precisa de rede e do projeto no ar.
   Também confere se o RLS do ranking ainda barra escrita com a chave pública. */
const { novaInstancia, novoPlacar, esperar } = require('./harness');
const P = novoPlacar('ONLINE');

const NUVEM = 'https://bcuqfqmtgjariuvnfjxm.supabase.co';
const CHAVE = 'sb_publishable_TbX62l3LBIZa84T0JQV1ig_WM7Ft-R4';

// roda os dois loops em paralelo, como dois navegadores abertos
async function rodar(A, B, segundos, aCada){
  const alvo = Math.round(segundos * 60);
  for(let i = 0; i < alvo; i++){
    A.quadros(1); B.quadros(1);
    if(aCada) aCada(i);
    if(i % 6 === 0) await esperar(16);   // devolve o controle pro socket
  }
}

(async () => {
  P.secao('0) o projeto está no ar?');
  let noAr = false;
  try{
    const r = await fetch(NUVEM + '/rest/v1/', { headers: { apikey: CHAVE } });
    noAr = r.status < 500;
    P.info('REST responde HTTP ' + r.status);
  }catch(e){
    P.info('projeto fora do ar: ' + (e.cause ? e.cause.code : e.message));
  }
  if(!noAr){
    console.log('\nSUPABASE FORA DO AR — suíte online pulada (isso NÃO é falha do jogo).');
    console.log('Religue em https://supabase.com/dashboard/project/bcuqfqmtgjariuvnfjxm');
    process.exitCode = 0;
    return;
  }

  P.secao('1) RLS do ranking ainda barra escrita com a chave pública');
  {
    const H = { apikey: CHAVE, Authorization: 'Bearer ' + CHAVE, 'Content-Type': 'application/json' };
    const url = NUVEM + '/rest/v1/jogo_ranking';
    const antes = await (await fetch(url + '?select=nome,score&order=score.desc', { headers: H })).text();
    const ins = await fetch(url, { method: 'POST', headers: H,
      body: JSON.stringify({ jogo: 'neon-devourer', nome: 'TESTE_RLS', score: 999999999 }) });
    await fetch(url + '?nome=neq.__x__', { method: 'PATCH', headers: H, body: JSON.stringify({ score: 1 }) });
    await fetch(url + '?nome=neq.__x__', { method: 'DELETE', headers: H });
    const dep = await (await fetch(url + '?select=nome,score&order=score.desc', { headers: H })).text();
    P.ok('INSERT recusado', ins.status >= 400, 'HTTP ' + ins.status);
    P.ok('ranking intacto após UPDATE e DELETE', antes === dep);
  }

  P.secao('2) host cria sala e convidado entra');
  const A = novaInstancia({ comRede: true });
  const B = novaInstancia({ comRede: true });
  A.X.save.charLevel = 10; B.X.save.charLevel = 10;
  A.X.save.ultimoNome = 'HST'; B.X.save.ultimoNome = 'CNV';
  A.X.save.nome = 'HST'; B.X.save.nome = 'CNV';

  await A.X.criarSala();
  const codigo = A.X.RT.codigo;
  P.ok('sala criada', !!codigo && A.X.RT.entrou, 'código ' + codigo);

  await B.X.entrarNaSala(codigo);
  await esperar(2500); await rodar(A, B, 0.5); await esperar(1500);
  P.ok('host viu o convidado', A.X.G.players.length === 2,
       A.X.G.players.map(p => p.nome).join(' + '));
  P.ok('convidado recebeu o elenco', B.X.G.players.length === 2);
  P.ok('convidado sabe que é o jogador 1', B.X.G.eu === 1, 'eu=' + B.X.G.eu);

  P.secao('3) o mapa viaja pela rede');
  A.X.comecarOnline();
  await esperar(2000); await rodar(A, B, 0.5); await esperar(1500);
  P.ok('convidado entrou em jogo', B.X.G.estado === 'jogando', B.X.G.estado);
  P.ok('mesmo bioma', A.X.G.bioma.id === B.X.G.bioma.id, A.X.G.bioma.id);
  P.ok('labirinto idêntico', JSON.stringify(A.X.G.grid) === JSON.stringify(B.X.G.grid));
  P.ok('mesma contagem de núcleos', A.X.G.restantes === B.X.G.restantes,
       A.X.G.restantes + ' = ' + B.X.G.restantes);

  P.secao('4) jogando junto por 6 segundos');
  A.X.G.players.forEach(p => p.invuln = 1e9);
  const dirs = ['left', 'up', 'right', 'down'];
  await rodar(A, B, 6, i => {
    if(i % 45 === 0){
      B.X.euJogador().quer = B.X.DIRS[dirs[(i / 45) % 4]];
      A.X.euJogador().quer = A.X.DIRS[dirs[(i / 45 + 2) % 4]];
    }
  });
  await esperar(800);
  const hostVeConvidado = A.X.G.players[1], convidadoSeVe = B.X.euJogador();
  const erro = Math.hypot(hostVeConvidado.x - convidadoSeVe.x,
                          hostVeConvidado.y - convidadoSeVe.y) / A.X.TILE;
  P.info(`host vê o convidado em (${hostVeConvidado.x|0},${hostVeConvidado.y|0})`);
  P.info(`convidado se vê em     (${convidadoSeVe.x|0},${convidadoSeVe.y|0})`);
  P.ok('posição sincronizada (< 2 tiles)', erro < 2, erro.toFixed(2) + ' tiles');

  let somaErro = 0;
  A.X.G.mobs.forEach((m, i) => {
    const o = B.X.G.mobs[i];
    if(o) somaErro += Math.hypot(m.x - o.x, m.y - o.y) / A.X.TILE;
  });
  const medio = somaErro / Math.max(1, A.X.G.mobs.length);
  P.ok('mobs sincronizados', medio < 2, medio.toFixed(2) + ' tiles de erro médio');

  P.secao('5) habilidade do convidado é executada pelo host');
  {
    const remoto = A.X.G.players[1];
    remoto.carga = 100; remoto.pulsoCd = 0;
    B.X.G.acoesPendentes = ['pulso'];
    await rodar(A, B, 1.2); await esperar(800);
    P.ok('host executou o pulso do convidado',
         remoto.pulsoCd > 0 || remoto.carga < 100,
         `pulsoCd=${remoto.pulsoCd.toFixed(2)} carga=${Math.round(remoto.carga)}`);
  }

  P.secao('6) convidado sai e o host libera o lugar');
  B.X.sairDaSala();
  await esperar(1500);
  await rodar(A, A, 0.3);
  P.ok('slot liberado', A.X.G.players.length === 1 && A.X.RT.pares.length === 0,
       `${A.X.G.players.length} jogador(es), ${A.X.RT.pares.length} par(es)`);

  A.X.RT.desligar(); B.X.RT.desligar();
  P.fim();
  setTimeout(() => process.exit(process.exitCode || 0), 300);
})();
