// Teste de rede: sobe o servidor de verdade, conecta 2 clientes, cria sala,
// entra, inicia a partida e confere que os retratos do mundo chegam.
//   node test/net.js
import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import WebSocket from 'ws';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PORT = 8099;
const BASE = `http://127.0.0.1:${PORT}`;

const espera = (ms) => new Promise((r) => setTimeout(r, ms));
let falhas = 0;
const ok = (cond, nome, extra = '') => {
  if (cond) console.log(`  ok   ${nome}`);
  else { falhas++; console.log(`  FALHA ${nome}${extra ? '\n       ' + extra : ''}`); }
};

console.log('\nAPOLLO TANKS — teste de rede\n');

const servidor = spawn(process.execPath, ['server/index.js'], {
  cwd: ROOT,
  env: { ...process.env, PORT: String(PORT), APOLLO_TICK_LOG: process.env.APOLLO_TICK_LOG || '' },
  stdio: ['ignore', 'pipe', 'pipe'],
});
let erroServidor = '';
servidor.stderr.on('data', (d) => { erroServidor += d; });

function cliente(nome) {
  const ws = new WebSocket(`ws://127.0.0.1:${PORT}/ws`);
  const c = { nome, ws, msgs: [], id: null, aberto: false };
  ws.on('open', () => { c.aberto = true; });
  ws.on('message', (raw) => {
    const m = JSON.parse(raw);
    c.msgs.push(m);
    if (m.t === 'hello') c.id = m.id;
  });
  c.send = (o) => ws.send(JSON.stringify(o));
  c.ultima = (tipo) => [...c.msgs].reverse().find((m) => m.t === tipo);
  c.conta = (tipo) => c.msgs.filter((m) => m.t === tipo).length;
  return c;
}

async function esperarPor(c, tipo, ms = 3000) {
  const fim = Date.now() + ms;
  while (Date.now() < fim) {
    const m = c.ultima(tipo);
    if (m) return m;
    await espera(30);
  }
  return null;
}

try {
  await espera(900);

  // --------------------------------------------------------- HTTP
  for (const rota of ['/', '/client/main.js', '/client/style.css', '/shared/sim.js', '/shared/maps.js']) {
    const r = await fetch(BASE + rota);
    ok(r.ok, `GET ${rota} → ${r.status}`);
  }
  const bloqueado = await fetch(BASE + '/package.json');
  ok(bloqueado.status === 404, 'GET /package.json é bloqueado (404)');

  // --------------------------------------------------------- sala
  const a = cliente('Ana');
  const b = cliente('Bia');
  await espera(400);
  ok(a.id && b.id, 'os dois clientes receberam id');

  a.send({ t: 'create', name: 'Ana', cls: 'pesado' });
  const salaA = await esperarPor(a, 'room');
  ok(!!salaA && salaA.code?.length === 4, 'sala criada com código de 4 letras', salaA && `code=${salaA.code}`);
  ok(salaA?.host === a.id, 'quem criou virou host');

  b.send({ t: 'join', code: salaA.code, name: 'Bia', cls: 'sniper' });
  await espera(300);
  const salaB = b.ultima('room');
  ok(salaB?.players.length === 2, 'a sala ficou com 2 jogadores', `veio ${salaB?.players.length}`);
  ok(salaB?.players.some((p) => p.cls === 'sniper'), 'a classe escolhida foi respeitada');

  b.send({ t: 'skin', skin: 'tigre' });
  await espera(250);
  ok(b.ultima('room')?.players.find((p) => p.id === b.id)?.skin === 'tigre', 'a skin do perfil chega na sala');
  b.send({ t: 'skin', skin: 'nao-existe' });
  await espera(250);
  ok(b.ultima('room')?.players.find((p) => p.id === b.id)?.skin === 'tigre', 'skin inventada é ignorada');

  const c = cliente('Cid');
  await espera(250);
  c.send({ t: 'join', code: 'ZZZZ', name: 'Cid' });
  const erro = await esperarPor(c, 'err', 1500);
  ok(!!erro, 'entrar em sala inexistente devolve erro', erro?.msg);

  // --------------------------------------------------------- partida
  // Iniciar abre a obra (t:'build'), não a fase (t:'start') — conferir a
  // ausência de 'start' aqui passaria mesmo se qualquer um pudesse iniciar.
  b.send({ t: 'start' });                       // não é host: deve ser ignorado
  await espera(300);
  ok(!a.ultima('build'), 'quem não é host não consegue iniciar');

  // --------------------------------------------------------- entradas tortas
  // Um nome que não é texto derrubava o processo inteiro (`.trim` de número).
  const chato = cliente('Chato');
  await espera(250);
  chato.send({ t: 'create', name: 12345 });
  chato.send({ t: 'create', name: { a: 1 } });
  chato.send({ t: 'create', name: ['x'] });
  chato.send({ t: 'join', code: 'ZZZZ', name: true });
  await espera(500);
  const vivo = await fetch(BASE + '/').then((r) => r.ok).catch(() => false);
  ok(vivo, 'nome que não é texto não derruba o servidor');
  ok(chato.ultima('room')?.players[0]?.name?.length > 0, 'o nome torto virou um nome válido',
    JSON.stringify(chato.ultima('room')?.players[0]?.name));
  chato.ws.close();

  // Entrar numa sala que recusa não pode expulsar você da sua.
  const dono = cliente('Dono');
  await espera(250);
  dono.send({ t: 'create', name: 'Dono' });
  const minhaSala = await esperarPor(dono, 'room');
  ok(!!minhaSala?.code, 'o teste conseguiu criar a segunda sala');

  dono.send({ t: 'join', code: minhaSala.code, name: 'Dono' });   // a própria sala
  await espera(400);
  const aindaExiste = cliente('Curioso');
  await espera(250);
  aindaExiste.send({ t: 'join', code: minhaSala.code, name: 'Curioso' });
  await espera(400);
  ok(aindaExiste.ultima('room')?.players.length === 2,
    'entrar no próprio código não apaga a sala',
    `veio ${aindaExiste.ultima('room')?.players.length}`);
  aindaExiste.ws.close();
  await espera(300);

  // --------------------------------------------------------- construção
  // Iniciar não cai direto na fase: abre o Modo Construção pro esquadrão
  // gastar a Sucata em defesa da águia.
  a.send({ t: 'start' });
  const obra = await esperarPor(a, 'build');
  ok(!!obra, 'iniciar abre o Modo Construção');
  ok(obra?.forts?.length === 5, 'as 5 fortificações vieram na lista', `veio ${obra?.forts?.length}`);
  ok(obra?.sucata === 0 && obra?.moeda?.nome === 'Sucata', 'a caixa começa vazia e a moeda tem nome');
  ok(obra?.forts?.every((f) => f.custo === f.niveis[0].custo), 'cada fortificação vem com o preço do próximo nível');
  ok(!!(await esperarPor(b, 'build', 1500)), 'o outro jogador também viu a obra');

  a.send({ t: 'comprar', id: 'aco' });         // sem caixa: tem que recusar
  const semGrana = await esperarPor(a, 'err', 1200);
  ok(!!semGrana, 'comprar sem sucata devolve erro', semGrana?.msg);

  a.send({ t: 'pronto' });
  await espera(250);
  ok(a.ultima('build')?.prontos.some((p) => p.id === a.id && p.pronto), 'o "estou pronto" aparece pro esquadrão');
  ok(!a.ultima('start'), 'a fase não começa antes de todo mundo estar pronto');
  b.send({ t: 'pronto' });

  const inicio = await esperarPor(a, 'start');
  ok(!!inicio, 'partida iniciou');
  ok(inicio?.tiles?.length === 676, 'mapa veio completo (26x26)', `veio ${inicio?.tiles?.length}`);
  ok(!!(await esperarPor(b, 'start', 1500)), 'o outro jogador também recebeu o início');

  // Fase 1: o host sobe pelo corredor livre (colunas 8-9 do mapa Fortaleza).
  // Fase 2: vira pra esquerda e atira no bloco de tijolo da coluna 6-7.
  // Ninguém atira em direção à águia — no original (e aqui) dá pra destruir a
  // própria base, e isso encerraria a partida no meio da medição.
  const marcaT = Date.now();
  const marcaSnap = a.msgs.filter((m) => m.t === 'snap').length;
  a.send({ t: 'in', d: 0, m: true, f: true });
  await espera(1700);
  const meioDoCaminho = a.ultima('snap').tanks.find((t) => t.i === 'p0');
  a.send({ t: 'in', d: 3, m: false, f: true });
  await espera(1500);
  a.send({ t: 'in', d: 3, m: false, f: false });
  await espera(150);

  ok(!a.ultima('end'), 'a partida seguiu viva durante a medição');

  const snaps = a.msgs.filter((m) => m.t === 'snap');
  const janela = snaps.slice(marcaSnap);
  const segundos = (Date.now() - marcaT) / 1000;

  // A simulação roda a 60 Hz e manda estado a cada 3 ticks.
  const ticksPorSeg = (janela[janela.length - 1].k - janela[0].k) / segundos;
  ok(ticksPorSeg > 52, `simulação a ${ticksPorSeg.toFixed(1)} ticks/s (alvo 60)`);
  ok(janela.length / segundos > 17, `${(janela.length / segundos).toFixed(1)} retratos/s (alvo 20)`);

  const t0 = janela[0].tanks.find((t) => t.i === 'p0');
  ok(meioDoCaminho.y < t0.y - 20, 'o tanque do host subiu pelo corredor', `y ${t0.y} → ${meioDoCaminho.y}`);
  ok(snaps.some((s) => s.b.length > 0), 'saíram tiros na rede');
  ok(snaps.some((s) => s.dt?.length > 0), 'mudanças no mapa (tijolo quebrado) foram enviadas');
  ok(snaps.some((s) => s.tanks.some((t) => !t.p)), 'inimigos apareceram na partida');
  ok(snaps.some((s) => s.fx?.length > 0), 'efeitos (tiro/explosão) chegaram');

  const ultimo = snaps[snaps.length - 1];
  ok(typeof ultimo.hud?.left === 'number' && ultimo.hud.left <= 30, 'HUD traz o contador de inimigos');
  ok(ultimo.tanks.find((t) => t.i === 'p0')?.n === 'Ana', 'nome do jogador chega no retrato');

  // --------------------------------------------------------- campanha
  ok(inicio.modo === 'campanha' && inicio.totalFases === 5 && inicio.stage === 0,
    'a partida começa na fase 1 da campanha', `modo=${inicio.modo} fase=${inicio.stage} de ${inicio.totalFases}`);
  ok(typeof ultimo.hud?.stage === 'number' && ultimo.hud.total === 5,
    'o retrato diz em que fase a sala está');
  ok(!!inicio.mapName && !!inicio.mapTag, 'a fase vem com nome e etiqueta do mapa');

  // --------------------------------------------------------- habilidade
  // A Ana é Pesado: a habilidade dela é o Bastião. Antes de usar, a recarga
  // tem que estar zerada; depois de usar, tem que estar contando.
  const antesHab = a.ultima('snap').tanks.find((t) => t.i === 'p0');
  ok(antesHab.ac === 0, 'a habilidade começa disponível', `ac=${antesHab.ac}`);
  a.send({ t: 'hab' });
  await espera(300);
  const depoisHab = a.ultima('snap').tanks.find((t) => t.i === 'p0');
  ok(depoisHab.ac > 0 && depoisHab.am > 0,
    'usar a habilidade dispara a recarga', `ac=${depoisHab.ac}/${depoisHab.am}`);
  ok(depoisHab.s === 1, 'o Bastião levantou o escudo');
  const cdNaHora = depoisHab.ac;
  a.send({ t: 'hab' });
  await espera(250);
  const terceira = a.ultima('snap').tanks.find((t) => t.i === 'p0');
  ok(terceira.ac < cdNaHora, 'a recarga não reinicia se apertar de novo', `ac=${terceira.ac}`);

  // --------------------------------------------------------- pausa
  const kAntes = a.ultima('snap').k;
  a.send({ t: 'pausa', on: true });
  const pausou = await esperarPor(a, 'pausa', 1200);
  ok(pausou?.on === true, 'o host consegue pausar');
  await espera(600);
  ok(a.ultima('snap').k - kAntes < 12, 'a simulação parou de verdade na pausa',
    `andou ${a.ultima('snap').k - kAntes} ticks`);
  b.send({ t: 'pausa', on: false });          // não é host: tem que ignorar
  await espera(300);
  ok(a.ultima('pausa').on === true, 'quem não é host não despausa');
  a.send({ t: 'pausa', on: false });
  await espera(400);
  ok(a.ultima('pausa').on === false, 'o host despausa');

  // --------------------------------------------------------- HUD estendido
  const hud = a.ultima('snap').hud;
  ok(typeof hud.sucata === 'number', 'o retrato traz a caixa de sucata');
  ok(hud.baseHp >= 1 && hud.baseHpMax >= 1, 'o retrato traz a vida da águia');
  ok(typeof hud.freeze === 'number' && typeof hud.pa === 'number', 'o retrato traz os efeitos do relógio e da pá');
  ok(a.ultima('snap').tanks.find((t) => t.i === 'p0')?.sp > 0,
    'o retrato traz a velocidade do tanque (a previsão do cliente depende dela)');

  // --------------------------------------------------------- recusa não expulsa
  // Com a partida rolando, a sala recusa gente nova. Quem tentar entrar não
  // pode perder a sala onde já estava.
  const teimoso = cliente('Teimoso');
  await espera(250);
  teimoso.send({ t: 'create', name: 'Teimoso' });
  const salaDele = await esperarPor(teimoso, 'room');
  ok(!!salaDele?.code, 'o teimoso criou a sala dele');

  teimoso.send({ t: 'join', code: salaA.code, name: 'Teimoso' });
  const recusa = await esperarPor(teimoso, 'err', 1500);
  ok(!!recusa, 'entrar em partida já começada devolve erro', recusa?.msg);

  teimoso.send({ t: 'cls', cls: 'sniper' });    // só funciona se ele ainda tem sala
  await espera(400);
  const salaDepois = teimoso.ultima('room');
  ok(salaDepois?.code === salaDele.code && salaDepois.players.some((p) => p.cls === 'sniper'),
    'a recusa não expulsou o jogador da sala dele',
    `code=${salaDepois?.code} esperado=${salaDele.code}`);
  teimoso.ws.close();

  // --------------------------------------------------------- reconexão
  // Com a partida rolando, cair não perde a vaga: ela fica guardada por um
  // minuto com pontos, vidas e upgrades.
  const vagaB = b.ultima('vaga');
  ok(!!vagaB?.token && vagaB.code === salaA.code, 'o jogador recebe o cracha da vaga');

  b.ws.close();
  await espera(600);
  const salaComQueda = a.ultima('room');
  ok(salaComQueda?.players.length === 2, 'a vaga de quem caiu continua na sala',
    `veio ${salaComQueda?.players.length}`);
  ok(salaComQueda?.players.some((p) => p.offline), 'a sala marca quem caiu como offline');

  const b2 = cliente('Bia de volta');
  await espera(300);
  b2.send({ t: 'voltar', code: salaA.code, token: vagaB.token });
  const voltou = await esperarPor(b2, 'start', 2500);
  ok(!!voltou, 'quem caiu volta pra partida em andamento');
  ok(voltou?.voltou === true, 'a volta é marcada como volta, não como fase nova');
  ok(voltou?.tiles?.length === 676, 'o campo volta inteiro, no estado atual');
  await espera(400);
  ok(a.ultima('room')?.players.every((p) => !p.offline), 'a sala parou de mostrar a queda');
  ok(!!(await esperarPor(b2, 'snap', 1500)), 'os retratos voltam a chegar pra quem reconectou');

  // cracha errado não toma a vaga de ninguém
  const ladrao = cliente('Ladrao');
  await espera(250);
  ladrao.send({ t: 'voltar', code: salaA.code, token: 'crachá-inventado' });
  const negado = await esperarPor(ladrao, 'err', 1500);
  ok(!!negado, 'cracha inválido não entra na vaga de outro', negado?.msg);
  ok(!ladrao.ultima('start'), 'e não recebe o estado da partida');
  ladrao.ws.close();

  a.ws.close();
  b2.ws.close();
  c.ws.close();
  await espera(300);
} catch (e) {
  falhas++;
  console.log(`  FALHA exceção: ${e.stack}`);
} finally {
  servidor.kill();
  await espera(200);
  if (erroServidor.trim()) { falhas++; console.log(`\n  stderr do servidor:\n${erroServidor}`); }
  console.log(falhas ? `\n${falhas} falha(s)\n` : '\nTudo certo.\n');
  process.exit(falhas ? 1 : 0);
}
