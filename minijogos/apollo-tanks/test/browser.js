// Teste de navegador: sobe o servidor, abre DUAS abas headless (dois jogadores
// de verdade), cria a sala, entra, joga alguns segundos e confere que a tela
// desenhou, que o tanque andou e que nenhum erro apareceu no console.
//   node test/browser.js
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import WebSocket from 'ws';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PORT = 8098;
const PORT_CAMPANHA = 8096;   // servidor com fase relâmpago, só pro intervalo
const espera = (ms) => new Promise((r) => setTimeout(r, ms));

let falhas = 0;
const ok = (cond, nome, extra = '') => {
  if (cond) console.log(`  ok   ${nome}`);
  else { falhas++; console.log(`  FALHA ${nome}${extra ? '\n       ' + extra : ''}`); }
};

const CANDIDATOS = [
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
];
const navegador = CANDIDATOS.find((p) => fs.existsSync(p));

console.log('\nAPOLLO TANKS — teste de navegador\n');
if (!navegador) {
  console.log('  Edge/Chrome não encontrado — teste pulado.\n');
  process.exit(0);
}

const servidor = spawn(process.execPath, ['server/index.js'], {
  cwd: ROOT, env: { ...process.env, PORT: String(PORT) }, stdio: ['ignore', 'pipe', 'pipe'],
});
servidor.stdout.on('data', () => {});
let erroServidor = '';
servidor.stderr.on('data', (d) => { erroServidor += d; });

// Segundo servidor com fase de zero inimigos: a fase se resolve sozinha e o
// intervalo entre fases aparece na hora, que é o que a gente quer conferir.
const servidorCampanha = spawn(process.execPath, ['server/index.js'], {
  cwd: ROOT,
  env: { ...process.env, PORT: String(PORT_CAMPANHA), APOLLO_INIMIGOS_POR_FASE: '0' },
  stdio: ['ignore', 'pipe', 'pipe'],
});
servidorCampanha.stdout.on('data', () => {});
servidorCampanha.stderr.on('data', (d) => { erroServidor += d; });

const perfil = fs.mkdtempSync(path.join(os.tmpdir(), 'apollo-'));
const chrome = spawn(navegador, [
  '--headless=new', `--user-data-dir=${perfil}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu',
  // Sem extensões: alguma instalada na máquina injeta requisição externa na
  // página e o console do jogo aparece sujo sem culpa nenhuma.
  '--disable-extensions', '--disable-component-extensions-with-background-pages',
  '--disable-background-timer-throttling', '--disable-backgrounding-occluded-windows',
  '--disable-renderer-backgrounding', '--disable-features=CalculateNativeWinOcclusion',
  // Porta 0 = o navegador escolhe uma livre e ANUNCIA no stderr. Com porta
  // fixa, um navegador esquecido de outra execução fica sentado nela: o teste
  // sobe um navegador novo, não consegue a porta, e sem perceber conversa com
  // o velho — perfil velho, localStorage velho, janelas acumuladas. Perdi uma
  // hora com um "sniper" que vazou de uma execução anterior por causa disso.
  '--remote-debugging-port=0',
  '--window-size=1200,900', 'about:blank',
], { stdio: ['ignore', 'ignore', 'pipe'] });

const CDP = await new Promise((res, rej) => {
  let buf = '';
  const prazo = setTimeout(() => rej(new Error('o navegador não anunciou a porta de depuração')), 20000);
  chrome.stderr.on('data', (d) => {
    buf += d;
    const m = buf.match(/ws:\/\/(127\.0\.0\.1:\d+)\/devtools\/browser\/\S+/);
    if (!m) return;
    clearTimeout(prazo);
    res({ ws: m[0], base: `http://${m[1]}` });
  });
});

// --------------------------------------------------------------- CDP mínimo

function conectarCDP(wsUrl, ehPagina = true) {
  let id = 0;
  const pend = new Map();
  const erros = [];
  let ws;

  const api = {
    erros,
    async cmd(method, params = {}) {
      const mid = ++id;
      ws.send(JSON.stringify({ id: mid, method, params }));
      return new Promise((res, rej) => {
        pend.set(mid, { res, rej });
        setTimeout(() => { if (pend.delete(mid)) rej(new Error(`timeout ${method}`)); }, 15000);
      });
    },
    async js(expr) {
      const r = await api.cmd('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
      if (r.exceptionDetails) throw new Error(`${expr} → ${r.exceptionDetails.text} ${r.exceptionDetails.exception?.description || ''}`);
      return r.result.value;
    },
    fechar() { try { ws.close(); } catch {} },
  };

  return (async () => {
    ws = new WebSocket(wsUrl, { maxPayload: 64 * 1024 * 1024 });
    await new Promise((res, rej) => { ws.on('open', res); ws.on('error', rej); });
    ws.on('message', (raw) => {
      const m = JSON.parse(raw);
      if (m.id && pend.has(m.id)) {
        const { res, rej } = pend.get(m.id);
        pend.delete(m.id);
        m.error ? rej(new Error(m.error.message)) : res(m.result);
        return;
      }
      if (m.method === 'Runtime.exceptionThrown') {
        erros.push(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text);
      }
      if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') {
        erros.push(m.params.args.map((a) => a.value ?? a.description).join(' '));
      }
      if (m.method === 'Log.entryAdded' && m.params.entry.level === 'error') {
        const txt = `${m.params.entry.text} ${m.params.entry.url || ''}`;
        // O jogo não fala com ninguém de fora: tudo vem do próprio servidor.
        // Então erro que cita um endereço que não é o nosso veio de fora da
        // página (extensão, proxy) e não conta como erro do jogo.
        if (!/https?:\/\//.test(txt) || txt.includes('127.0.0.1')) erros.push(txt);
      }
    });
    // A conexão com o navegador (a que abre janelas) não tem esses domínios.
    if (ehPagina) {
      await api.cmd('Runtime.enable');
      await api.cmd('Log.enable');
      await api.cmd('Page.enable');
    }
    return api;
  })();
}

// Só UMA página do Chrome fica em primeiro plano de cada vez — e é uma disputa
// global, nem abrir um segundo navegador dá duas. Quem fica atrás recebe
// `document.hidden` e tem o requestAnimationFrame congelado: o laço do jogo
// para, nenhum comando sai e nada é desenhado. E não adianta screencast.
//
// Então cada jogador é exercitado enquanto está na frente, com `aoVivo()`. O
// que não depende do laço — lobby, HUD, placar — chega por WebSocket e continua
// valendo com a página atrás, por isso essas partes se conferem a qualquer hora.
//
// Cada página vai numa JANELA própria: com duas abas na mesma janela, até a da
// frente fica capada (~7 fps em vez de 60).
let browser = null;
async function abaCDP(url) {
  if (!browser) {
    browser = await conectarCDP(CDP.ws, false);
  }
  const { targetId } = await browser.cmd('Target.createTarget', { url, newWindow: true, width: 1200, height: 900 });
  const lista = await fetch(`${CDP.base}/json/list`).then((r) => r.json());
  const alvo = lista.find((t) => t.id === targetId);
  if (!alvo) throw new Error('janela criada mas não apareceu em /json/list');
  return conectarCDP(alvo.webSocketDebuggerUrl);
}

// Põe a janela deste jogador na frente e espera o laço voltar a rodar.
async function aoVivo(p) {
  await p.cmd('Page.bringToFront');
  await espera(500);
}

// Quantos quadros por segundo o laço do jogo está conseguindo rodar.
const MEDIR_FPS = `new Promise((res) => {
  let n = 0; const t0 = performance.now();
  const passo = () => { n++; if (performance.now() - t0 < 600) requestAnimationFrame(passo);
                        else res(Math.round(n * 1000 / (performance.now() - t0))); };
  requestAnimationFrame(passo);
  setTimeout(() => res(0), 1500);   // página congelada nunca chama o passo
})`;

const TECLA = (aba, tipo, code, key) =>
  aba.cmd('Input.dispatchKeyEvent', {
    type: tipo, code, key,
    windowsVirtualKeyCode: { ArrowUp: 38, ArrowDown: 40, ArrowLeft: 37, ArrowRight: 39, Space: 32, ShiftLeft: 16, Tab: 9 }[code] || 0,
  });

// --------------------------------------------------------------- roteiro

try {
  await espera(1500);
  const p1 = await abaCDP(`http://127.0.0.1:${PORT}/`);
  await espera(1200);
  ok(p1.erros.length === 0, 'página 1 carregou sem erro de console', p1.erros.join('\n       '));
  ok(await p1.js(`!!document.getElementById('tela').getContext('2d')`), 'canvas disponível');

  await p1.js(`document.getElementById('nome').value='Ana'; document.getElementById('btn-criar').click(); true`);
  await espera(900);
  const codigo = await p1.js(`document.getElementById('lobby-codigo').textContent`);
  ok(/^[A-Z0-9]{4}$/.test(codigo), `sala criada na interface (${codigo})`);
  ok(await p1.js(`!document.getElementById('lobby').classList.contains('hidden')`), 'tela de lobby apareceu');
  ok(await p1.js(`document.querySelectorAll('#classes .classe').length === 4`), 'as 4 classes aparecem pra escolher');

  // segundo jogador entra pelo link
  const p2 = await abaCDP(`http://127.0.0.1:${PORT}/?sala=${codigo}`);
  await espera(1200);
  await p2.js(`document.getElementById('nome').value='Bia'; document.getElementById('btn-entrar').click(); true`);
  await espera(900);
  ok(await p2.js(`document.getElementById('codigo').value`) === codigo, 'o link ?sala= já preencheu o código');
  ok(await p2.js(`document.querySelectorAll('#lobby-jogadores li').length`) === 2, 'jogador 2 entrou pelo link com código');

  // troca de classe no lobby tem que aparecer pros dois
  await p2.js(`document.querySelector('[data-cls="sniper"]').click(); true`);
  await espera(600);
  ok(await p1.js(`document.getElementById('lobby-jogadores').textContent.includes('Sniper')`), 'troca de classe apareceu pro outro jogador');
  ok(await p1.js(`document.querySelectorAll('#lobby-jogadores li').length`) === 2, 'jogador 1 viu a entrada do jogador 2');
  ok(await p2.js(`document.getElementById('host-area').classList.contains('hidden')`), 'só o host vê o botão de iniciar');

  // a campanha é o padrão e as 5 fases aparecem na trilha do lobby
  ok(await p1.js(`document.getElementById('btn-campanha').classList.contains('on')`), 'a campanha vem escolhida por padrão');
  ok(await p1.js(`document.querySelectorAll('#campanha-lista .fase-chip').length`) === 5, 'as 5 fases da campanha aparecem no lobby');
  ok(await p1.js(`document.querySelectorAll('#campanha-lista .fase-chip.chefe').length`) === 1, 'a fase do chefe vem marcada');

  // ---------------------------------------------- modo construção
  await p1.js(`document.getElementById('btn-iniciar').click(); true`);
  await espera(1200);
  ok(await p1.js(`!document.getElementById('construcao').classList.contains('hidden')`), 'iniciar abre o Modo Construção');
  ok(await p1.js(`document.querySelectorAll('#build-cartas .fort').length`) === 5, 'as 5 fortificações aparecem na obra');
  ok(/⛭/.test(await p1.js(`document.getElementById('build-sucata').textContent`)), 'a caixa de sucata aparece');
  ok(await p1.js(`document.querySelectorAll('#build-cartas .fort[disabled]').length`) === 5,
    'sem sucata, nada pode ser comprado');
  ok(await p1.js(`(() => { const c = document.getElementById('build-previa');
    const d = c.getContext('2d').getImageData(0,0,c.width,c.height).data;
    let n = 0; for (let i = 0; i < d.length; i += 4) if (d[i]+d[i+1]+d[i+2] > 60) n++;
    return n; })()`) > 300, 'a prévia da águia foi desenhada');

  await p1.js(`document.getElementById('btn-pronto').click(); true`);
  await espera(400);
  ok(await p1.js(`document.getElementById('btn-pronto').disabled`), 'quem já disse pronto não clica de novo');
  ok(await p1.js(`document.getElementById('jogo').classList.contains('hidden')`), 'a fase espera o esquadrão todo');
  await p2.js(`document.getElementById('btn-pronto').click(); true`);
  await espera(1200);

  ok(await p1.js(`!document.getElementById('jogo').classList.contains('hidden')`), 'partida abriu no jogador 1');
  ok(await p2.js(`!document.getElementById('jogo').classList.contains('hidden')`), 'partida abriu no jogador 2');
  ok(await p1.js(`document.getElementById('hud-mapa').textContent.length > 2`), 'HUD mostra o nome do mapa');

  // ---------------------------------------------- a vez do jogador 1
  await aoVivo(p1);
  const fps1 = await p1.js(MEDIR_FPS);
  ok(fps1 > 45, `a tela do jogador 1 roda solta (${fps1} fps)`);

  // O campo tem que ter cor: se ficasse tudo preto, o desenho não rodou.
  const pintados = await p1.js(`(() => {
    const d = document.getElementById('tela').getContext('2d').getImageData(0,0,208,208).data;
    let n = 0; for (let i = 0; i < d.length; i += 4) if (d[i]+d[i+1]+d[i+2] > 40) n++;
    return n;
  })()`);
  ok(pintados > 3000, `o campo foi desenhado (${pintados} pixels com cor)`);

  // anda pra cima por 2s no jogador 1
  const antes = await p1.js(`APOLLO.local ? APOLLO.local.y : null`);
  await TECLA(p1, 'keyDown', 'ArrowUp', 'ArrowUp');
  await TECLA(p1, 'keyDown', 'Space', ' ');
  await espera(2000);
  const depois = await p1.js(`APOLLO.local ? APOLLO.local.y : null`);
  const visto = await p1.js(`JSON.stringify({ oculta: document.hidden, emJogo: APOLLO.emJogo, foco: document.activeElement.id })`);
  await TECLA(p1, 'keyUp', 'ArrowUp', 'ArrowUp');
  await TECLA(p1, 'keyUp', 'Space', ' ');
  await espera(400);

  ok(antes !== null && depois !== null && depois < antes - 40,
    'o tanque do jogador 1 subiu de verdade', `y ${antes} → ${depois} · ${visto}`);

  const ping = await p1.js(`document.getElementById('hud-ping').textContent`);
  ok(/ms/.test(ping), `ping medido (${ping})`);
  ok(await p1.js(`document.querySelectorAll('#hud-times li').length`) === 2, 'HUD lista os 2 jogadores');
  ok(await p1.js(`document.querySelectorAll('#hud-inimigos i').length > 0`) === true, 'HUD conta os inimigos restantes');

  // O tanque do jogador 1 é amarelo (#f2c14e) e nasce embaixo. Se ele subiu,
  // esse amarelo tem que aparecer na parte de cima do campo — e sumir de baixo.
  // (A águia também é amarelada, por isso a faixa de baixo começa depois dela.)
  const amarelo = (y, h) => `(() => {
    const d = document.getElementById('tela').getContext('2d').getImageData(0, ${y}, 208, ${h}).data;
    let n = 0; for (let i = 0; i < d.length; i += 4) if (d[i] > 180 && d[i+1] > 140 && d[i+2] < 120) n++;
    return n;
  })()`;
  const emCima = await p1.js(amarelo(0, 170));
  const ondeNasceu = await p1.js(amarelo(186, 16));
  ok(emCima > 40, 'tanque do jogador 1 subiu pelo corredor', `pixels amarelos em cima=${emCima}`);
  ok(ondeNasceu < 10, 'e saiu de onde nasceu', `pixels amarelos embaixo=${ondeNasceu}`);

  // ---------------------------------------------- habilidade e fase
  ok(await p1.js(`document.getElementById('hud-fase').textContent`) === '1/5', 'o HUD mostra a fase 1 de 5');
  // o jogador 1 ficou com a classe padrão (Assalto), cuja habilidade é o Arranque
  const habNome = await p1.js(`document.getElementById('hab-nome').textContent`);
  ok(habNome === 'Arranque', `o HUD traz a habilidade da classe (${habNome})`);
  ok(await p1.js(`document.getElementById('hud-hab').classList.contains('pronta')`), 'a habilidade começa pronta');

  // Shift dispara a habilidade: a cortina de recarga sobe e o escudo acende.
  await TECLA(p1, 'keyDown', 'ShiftLeft', 'Shift');
  await TECLA(p1, 'keyUp', 'ShiftLeft', 'Shift');
  await espera(500);
  const recarga = await p1.js(`parseFloat(document.getElementById('hab-recarga').style.height) || 0`);
  ok(recarga > 5, `apertar Shift põe a habilidade pra recarregar (${recarga.toFixed(0)}%)`);
  ok(!(await p1.js(`document.getElementById('hud-hab').classList.contains('pronta')`)), 'e ela deixa de aparecer como pronta');

  // ---------------------------------------------- a vez do jogador 2
  // Agora na frente é a janela da Bia. O jogador 2 não é só um espectador: a
  // tela dele tem que desenhar e o tanque dele tem que responder ao teclado.
  await aoVivo(p2);
  const fps2 = await p2.js(MEDIR_FPS);
  ok(fps2 > 45, `a tela do jogador 2 roda solta (${fps2} fps)`);

  const pintados2 = await p2.js(`(() => {
    const d = document.getElementById('tela').getContext('2d').getImageData(0,0,208,208).data;
    let n = 0; for (let i = 0; i < d.length; i += 4) if (d[i]+d[i+1]+d[i+2] > 40) n++;
    return n;
  })()`);
  ok(pintados2 > 3000, `o campo foi desenhado no jogador 2 (${pintados2} pixels com cor)`);

  // O jogador 2 nasce na coluna 16, que também tem corredor livre até em cima.
  const antes2 = await p2.js(`APOLLO.local ? APOLLO.local.y : null`);
  await TECLA(p2, 'keyDown', 'ArrowUp', 'ArrowUp');
  await espera(2000);
  const depois2 = await p2.js(`APOLLO.local ? APOLLO.local.y : null`);
  await TECLA(p2, 'keyUp', 'ArrowUp', 'ArrowUp');
  await espera(400);
  ok(antes2 !== null && depois2 !== null && depois2 < antes2 - 40,
    'o tanque do jogador 2 também anda', `y ${antes2} → ${depois2}`);

  // E o jogador 1, que ficou pra trás, viu o tanque da Bia sair do lugar —
  // prova de que o retrato do servidor chega mesmo com a janela atrás.
  const viuOOutro = await p1.js(`(() => {
    const s = APOLLO.snaps[APOLLO.snaps.length - 1];
    const t = s && s.tanks.find((t) => t.i === 'p1');
    return t ? t.y : null;
  })()`);
  ok(viuOOutro !== null && viuOOutro < antes2 - 40,
    'o jogador 1 viu o tanque do 2 se mexer', `y do p1 visto pelo jogador 1 = ${viuOOutro}`);

  ok(p1.erros.length === 0, 'nenhum erro de console durante a partida (jogador 1)', p1.erros.join('\n       '));
  ok(p2.erros.length === 0, 'nenhum erro de console durante a partida (jogador 2)', p2.erros.join('\n       '));
  p1.fechar(); p2.fechar();

  // ---------------------------------------------- virada de fase
  // No servidor de fase relâmpago a fase 1 se limpa sozinha, então dá pra ver
  // o intervalo, escolher um upgrade e cair na fase 2.
  const p3 = await abaCDP(`http://127.0.0.1:${PORT_CAMPANHA}/`);
  await espera(1200);
  await aoVivo(p3);
  await p3.js(`document.getElementById('nome').value='Caio'; document.getElementById('btn-criar').click(); true`);
  await espera(900);
  await p3.js(`document.getElementById('btn-iniciar').click(); true`);
  await espera(900);
  await p3.js(`document.getElementById('btn-pronto').click(); true`);
  await espera(1500);

  ok(await p3.js(`!document.getElementById('intervalo').classList.contains('hidden')`), 'limpar a fase abre o intervalo');
  const tituloInter = await p3.js(`document.getElementById('inter-titulo').textContent`);
  ok(tituloInter === 'FASE 1 LIMPA', `o intervalo diz que fase caiu (${tituloInter})`);
  ok(await p3.js(`document.querySelectorAll('#inter-cartas .carta').length`) === 3, 'vêm 3 cartas de upgrade');
  ok(/\d+s/.test(await p3.js(`document.getElementById('inter-relogio').textContent`)), 'o relógio do intervalo está correndo');

  await p3.js(`document.querySelector('#inter-cartas .carta').click(); true`);
  await espera(400);
  ok(await p3.js(`document.querySelector('#inter-cartas .carta').classList.contains('on')`), 'a carta escolhida fica marcada');
  ok(await p3.js(`document.getElementById('inter-escolhas').textContent.includes('Caio')`), 'o esquadrão mostra quem já escolheu');

  // a conta da Sucata e o placar por tipo aparecem no intervalo
  ok(await p3.js(`document.querySelectorAll('#inter-conta li').length`) >= 2, 'a conta da Sucata veio discriminada');
  ok(await p3.js(`document.getElementById('inter-conta').textContent.includes('Caixa do esquadrão')`),
    'o intervalo mostra o total da caixa');
  ok(await p3.js(`document.querySelectorAll('#inter-tipos li').length`) >= 1, 'o placar por tipo de inimigo aparece');

  // 1,4s de respiro e o esquadrão volta pra obra, agora com dinheiro no caixa
  await espera(2400);
  ok(await p3.js(`!document.getElementById('construcao').classList.contains('hidden')`),
    'do intervalo o esquadrão volta pro Modo Construção');
  const saldo = await p3.js(`APOLLO.sala ? document.getElementById('build-sucata').textContent : ''`);
  ok(/[1-9]/.test(saldo), `agora tem sucata pra gastar (${saldo})`);
  const compraveis = await p3.js(`document.querySelectorAll('#build-cartas .fort:not([disabled])').length`);
  ok(compraveis > 0, `com caixa, dá pra comprar (${compraveis} opções)`);

  await p3.js(`document.querySelector('#build-cartas .fort:not([disabled])').click(); true`);
  await espera(500);
  const nivelComprado = await p3.js(`document.querySelectorAll('#build-cartas .niveis em.on').length`);
  ok(nivelComprado >= 1, 'a compra sobe o nível da fortificação');

  await p3.js(`document.getElementById('btn-pronto').click(); true`);
  await espera(1800);
  const tituloFase2 = await p3.js(`document.getElementById('inter-titulo').textContent`);
  ok(tituloFase2 === 'FASE 2 LIMPA', `a campanha seguiu pra fase seguinte (${tituloFase2})`);
  ok(await p3.js(`APOLLO.sala && APOLLO.hud.stage`) >= 1, 'o cliente sabe que mudou de fase');
  ok(p3.erros.length === 0, 'nenhum erro de console na virada de fase', p3.erros.join('\n       '));
  p3.fechar();

  // ---------------------------------------------- perfil e manual
  const p4 = await abaCDP(`http://127.0.0.1:${PORT}/`);
  await espera(1200);
  await aoVivo(p4);

  await p4.js(`document.getElementById('btn-perfil').click(); true`);
  await espera(400);
  ok(await p4.js(`!document.getElementById('perfil').classList.contains('hidden')`), 'a tela de perfil abre');
  ok(await p4.js(`document.querySelectorAll('#perfil-skins .skin').length`) === 6, 'as 6 skins aparecem');
  ok(await p4.js(`document.querySelectorAll('#perfil-skins .skin img').length`) === 6, 'cada skin vem com o desenho do tanque');
  ok(await p4.js(`document.querySelectorAll('#perfil-ficha li').length`) >= 5, 'a ficha de carreira aparece');

  await p4.js(`document.querySelector('[data-skin="tigre"]').click(); true`);
  await espera(300);
  ok(await p4.js(`localStorage.getItem('apollo-skin')`) === 'tigre', 'a skin escolhida fica guardada');
  ok(await p4.js(`document.querySelector('[data-skin="tigre"]').classList.contains('on')`), 'a skin escolhida fica marcada');

  await p4.js(`document.getElementById('btn-perfil-voltar').click(); true`);
  await espera(300);
  ok(await p4.js(`!document.getElementById('menu').classList.contains('hidden')`), 'o perfil volta pro menu');

  await p4.js(`document.getElementById('btn-manual').click(); true`);
  await espera(400);
  ok(await p4.js(`!document.getElementById('manual').classList.contains('hidden')`), 'o manual abre');
  ok(await p4.js(`document.querySelectorAll('#manual-corpo .verbete').length`) >= 4, 'o manual começa nas classes');
  for (const aba of ['bonus', 'terreno', 'defesa']) {
    await p4.js(`document.querySelector('[data-aba="${aba}"]').click(); true`);
    await espera(250);
    const n = await p4.js(`document.querySelectorAll('#manual-corpo .verbete').length`);
    ok(n >= 3, `a aba ${aba} do manual tem conteúdo (${n} verbetes)`);
  }
  // ---------------------------------------------- som
  ok(await p4.js(`document.getElementById('btn-som').textContent.includes('Som')`), 'o botão de som começa ligado');
  await p4.js(`document.getElementById('btn-som').click(); true`);
  await espera(250);
  ok(await p4.js(`localStorage.getItem('apollo-som')`) === 'off', 'o botão desliga o som e guarda a escolha');
  ok(await p4.js(`document.getElementById('btn-som').textContent.includes('Mudo')`), 'o botão passa a dizer Mudo');
  ok(await p4.js(`document.getElementById('btn-som-jogo').textContent.includes('Mudo')`), 'o botão do HUD acompanha');

  await TECLA(p4, 'keyDown', 'KeyM', 'm');
  await TECLA(p4, 'keyUp', 'KeyM', 'm');
  await espera(250);
  ok(await p4.js(`localStorage.getItem('apollo-som')`) === 'on', 'a tecla M religa o som');
  ok(await p4.js(`typeof (window.AudioContext || window.webkitAudioContext) === 'function'`),
    'o navegador do teste tem WebAudio (o som é sintetizado, sem arquivo)');

  // ---------------------------------------------- teclado e foco
  // O jogo é jogado no teclado; navegar por ele nas telas também tem que dar.
  await p4.js(`document.getElementById('btn-manual-voltar').click(); true`);
  await espera(300);
  ok(await p4.js(`document.activeElement.id`) === 'menu',
    'trocar de tela leva o foco junto, em vez de largar no <body>');

  // Tab a partir do menu tem que cair num controle da tela visível.
  await TECLA(p4, 'rawKeyDown', 'Tab', 'Tab');
  await TECLA(p4, 'keyUp', 'Tab', 'Tab');
  await espera(200);
  const focado = await p4.js(`(() => { const a = document.activeElement;
    return { id: a.id, tela: a.closest('.screen') ? a.closest('.screen').id : null,
             oculto: !!a.closest('.hidden') }; })()`);
  ok(focado.tela === 'menu' && !focado.oculto,
    `o Tab fica dentro da tela visível (foi pra "${focado.id}" em ${focado.tela})`);

  const anel = await p4.js(`(() => {
    const b = document.getElementById('btn-criar');
    b.focus();
    const e = getComputedStyle(b);
    return { largura: e.outlineWidth, estilo: e.outlineStyle, sombra: e.boxShadow };
  })()`);
  ok(parseFloat(anel.largura) >= 2 && anel.estilo !== 'none',
    `botão focado tem anel visível (${anel.largura} ${anel.estilo})`);
  ok(anel.sombra && anel.sombra !== 'none',
    'o anel tem camada escura por baixo, pra aparecer no botão dourado também');

  ok(p4.erros.length === 0, 'nenhum erro de console no perfil e no manual', p4.erros.join('\n       '));
  p4.fechar();

  // ---------------------------------------------- queda e volta
  // Derruba a rede da aba de verdade e confere que a partida é retomada
  // sozinha, na mesma tela e com a mesma vaga.
  const p5 = await abaCDP(`http://127.0.0.1:${PORT}/`);
  await espera(1200);
  await aoVivo(p5);
  await p5.js(`document.getElementById('nome').value='Dino'; document.getElementById('btn-criar').click(); true`);
  await espera(900);
  const vaga = await p5.js(`sessionStorage.getItem('apollo-vaga')`);
  ok(!!vaga && /token/.test(vaga), 'a aba guarda a vaga ao entrar na sala');

  await p5.js(`document.getElementById('btn-iniciar').click(); true`);
  await espera(1000);
  ok(await p5.js(`!document.getElementById('construcao').classList.contains('hidden')`), 'a sala foi pro Modo Construção');

  // Derruba o socket na mão. `Network.emulateNetworkConditions` com offline
  // barra HTTP mas NÃO fecha WebSocket que já está aberto: a queda só viria
  // quando a rede voltasse, tarde demais pra ver o aviso.
  await p5.js(`APOLLO.net.ws.close(); true`);

  let avisou = false;
  for (let i = 0; i < 20 && !avisou; i++) {
    avisou = await p5.js(`!document.getElementById('reconectando').classList.contains('hidden')`);
    if (!avisou) await espera(150);
  }
  ok(avisou, 'a aba avisa que caiu e está tentando voltar');
  ok(/\d+\/\d+/.test(await p5.js(`document.getElementById('reconectando').textContent`)),
    'o aviso diz em que tentativa está');

  await espera(3000);
  ok(await p5.js(`document.getElementById('reconectando').classList.contains('hidden')`),
    'o aviso some quando a conexão volta');
  ok(await p5.js(`!document.getElementById('construcao').classList.contains('hidden')`),
    'a aba volta pra tela em que a sala está, não pro menu');
  const vagaDepois = await p5.js(`sessionStorage.getItem('apollo-vaga')`);
  ok(vagaDepois === vaga, 'a vaga continua a mesma depois de voltar');
  ok(await p5.js(`document.querySelectorAll('#build-prontos li').length`) === 1,
    'o esquadrão voltou a listar o jogador');
  p5.fechar();
} catch (e) {
  falhas++;
  console.log(`  FALHA exceção: ${e.stack}`);
} finally {
  chrome.kill();
  servidor.kill();
  servidorCampanha.kill();
  await espera(400);
  try { fs.rmSync(perfil, { recursive: true, force: true }); } catch {}
  if (erroServidor.trim()) { falhas++; console.log(`\n  stderr do servidor:\n${erroServidor}`); }
  console.log(falhas ? `\n${falhas} falha(s)\n` : '\nTudo certo.\n');
  process.exit(falhas ? 1 : 0);
}
