// Cliente: telas, entrada do teclado, previsão do próprio tanque e desenho.

import {
  TICK_MS, INTERP_MS, FIELD, CLASSES, CLASS_IDS, ABILITIES, DASH_TICKS,
  SKINS, BONUS, BONUS_IDS, FORTIFICACOES, NIVEIS_TANQUE, MOEDA, PONTOS_INIMIGO,
} from '../shared/constants.js';
import { moveTank } from '../shared/sim.js';
import { createRenderer, createBasePreview, pintarTanque } from './render.js';
import { createAudio } from './audio.js';
import { connect } from './net.js';

const el = (id) => document.getElementById(id);
const telas = {
  menu: el('menu'), perfil: el('perfil'), manual: el('manual'), lobby: el('lobby'),
  construcao: el('construcao'), jogo: el('jogo'), intervalo: el('intervalo'), fim: el('fim'),
};

let telaAtual = 'menu';

function mostrar(nome) {
  const mudou = telaAtual !== nome;
  telaAtual = nome;
  for (const [k, node] of Object.entries(telas)) node.classList.toggle('hidden', k !== nome);
  // Sair do campo solta o que estiver segurado. Sem isto, uma tecla apertada
  // na tela de construção fazia o tanque sair andando e atirando sozinho
  // assim que a fase começava.
  if (nome !== 'jogo') soltarTudo();
  // O foco do teclado vai junto com a tela. Só na TROCA: mensagem de sala
  // chega o tempo todo, e refocar a cada uma roubaria o foco de quem está
  // navegando por tab.
  if (mudou) telas[nome].focus({ preventScroll: true });
}

// Botão que vira `disabled` (o "Estou pronto", a carta já escolhida) leva o
// foco junto pro nada — o teclado cai no <body> e a pessoa perde o lugar.
function resgatarFoco() {
  const a = document.activeElement;
  if (!a || a === document.body || a.disabled || a.closest?.('.hidden')) {
    telas[telaAtual]?.focus({ preventScroll: true });
  }
}

// Faixa fixa no topo enquanto a conexão está caindo. Passar `null` some com ela.
function avisarReconexao(msg) {
  const node = el('reconectando');
  node.textContent = msg || '';
  node.classList.toggle('hidden', !msg);
}

let toastTimer = null;
function toast(msg) {
  const node = el('toast');
  node.textContent = msg;
  node.classList.remove('hidden');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => node.classList.add('hidden'), 2200);
}

// ------------------------------------------------------------------ perfil
// Fica só no navegador: nome, skin, recorde e a ficha de carreira.

const FICHA_VAZIA = { partidas: 0, abates: 0, pontos: 0, sucata: 0, fases: 0, campanhas: 0 };

function lerFicha() {
  try { return { ...FICHA_VAZIA, ...JSON.parse(localStorage.getItem('apollo-ficha') || '{}') }; }
  catch { return { ...FICHA_VAZIA }; }
}
function gravarFicha(f) { localStorage.setItem('apollo-ficha', JSON.stringify(f)); }
const lerRecorde = () => Number(localStorage.getItem('apollo-recorde') || 0);

// ------------------------------------------------------------------ estado

const S = {
  meuId: null,
  meuSlot: null,
  meuTanque: null,       // 'p0'..'p3'
  sala: null,
  classe: localStorage.getItem('apollo-classe') || 'assalto',
  skin: localStorage.getItem('apollo-skin') || 'liso',
  nome: localStorage.getItem('apollo-nome') || '',
  modo: 'campanha',
  snaps: [],             // histórico curto pra interpolar os outros
  local: null,           // meu tanque previsto localmente
  baseViva: true,
  hud: { left: 0, queue: 0, base: 1, stage: 0, total: 5, boss: null, baseHp: 1, baseHpMax: 1 },
  bonus: [],
  minas: [],
  torres: [],
  mapa: '',
  emJogo: false,
  pausado: false,
  hab: { id: null, cd: 0, max: 1 },   // recarga da minha habilidade
  nivel: 0,
  fimFaixa: 0,           // até quando a faixa de fase fica na tela
  voltaPara: 'menu',     // pra onde o botão Voltar do perfil/manual leva
};

const canvas = el('tela');
const R = createRenderer(canvas);
const previaBase = createBasePreview(el('build-previa'));
const A = createAudio();

// O navegador só libera áudio depois de um gesto. Destrava no primeiro
// clique ou tecla e não incomoda mais.
for (const ev of ['pointerdown', 'keydown']) {
  addEventListener(ev, () => A.iniciar(), { once: true });
}

// Tudo que é da partida em si. Chamado ao sair do campo, pra nenhuma sobra
// (faixa, barra do chefe, pausa, previsão) aparecer na fase ou na sala seguinte.
function limparPartida() {
  S.emJogo = false;
  S.pausado = false;
  S.local = null;
  S.snaps = [];
  S.bonus = []; S.minas = []; S.torres = [];
  S.fimFaixa = 0;
  el('faixa').classList.add('hidden');
  el('pausa').classList.add('hidden');
  el('chefe-barra').classList.add('hidden');
}

// ------------------------------------------------------------------ rede

// A vaga guardada fica na aba (sessionStorage, não localStorage): duas abas
// abertas são dois jogadores, não um querendo tomar a vaga do outro.
const VAGA = 'apollo-vaga';
const lerVaga = () => { try { return JSON.parse(sessionStorage.getItem(VAGA) || 'null'); } catch { return null; } };
const guardarVaga = (v) => sessionStorage.setItem(VAGA, JSON.stringify(v));
const esquecerVaga = () => sessionStorage.removeItem(VAGA);

const net = connect({
  hello: (m) => {
    S.meuId = m.id;
    // Se esta aba já tinha vaga numa sala, tenta voltar pra ela antes de
    // qualquer outra coisa. O servidor responde com o estado atual da sala.
    const vaga = lerVaga();
    if (vaga?.code && vaga?.token) net.send({ t: 'voltar', code: vaga.code, token: vaga.token });
  },

  // Cracha da vaga, mandado só pra este jogador.
  vaga: (m) => {
    S.meuId = m.id;
    guardarVaga({ code: m.code, token: m.token });
    avisarReconexao(null);
  },

  reconectando: (n, max) => avisarReconexao(`Conexão caiu. Tentando voltar… (${n}/${max})`),

  // Erro fora do menu vira aviso passageiro. Escrever sempre no #menu-erro
  // deixava a mensagem gravada lá, pra reaparecer quando o jogador voltasse.
  err: (m) => {
    // A vaga não existe mais: limpa e volta pro menu em vez de insistir.
    if (/vaga|sala não existe/i.test(m.msg)) {
      esquecerVaga();
      avisarReconexao(null);
      if (!S.sala) { el('menu-erro').textContent = m.msg; mostrar('menu'); }
      return;
    }
    if (telas.menu.classList.contains('hidden')) toast(m.msg);
    else el('menu-erro').textContent = m.msg;
  },

  room: (m) => {
    S.sala = m;
    S.modo = m.modo || 'campanha';
    const eu = m.players.find((p) => p.id === S.meuId);
    if (!eu) return;
    S.meuSlot = eu.slot;
    S.meuTanque = `p${eu.slot}`;
    S.classe = eu.cls;
    // Esta mensagem também chega no meio da partida (quando alguém entra ou
    // sai). Só volta pro lobby se a sala realmente estiver no lobby.
    S.emJogo = m.state === 'playing';
    desenharLobby(m);
    // Atualização de sala não arranca ninguém do perfil nem do manual: essas
    // telas são abertas de propósito e chegam mensagens de sala o tempo todo.
    const lendo = !telas.perfil.classList.contains('hidden') || !telas.manual.classList.contains('hidden');
    if (m.state === 'lobby' && !lendo) mostrar('lobby');
    if (m.state === 'ended') {
      const souHost = m.host === S.meuId;
      el('btn-denovo').classList.toggle('hidden', !souHost);
      el('fim-aviso').classList.toggle('hidden', souHost);
    }
    history.replaceState(null, '', `?sala=${m.code}`);
  },

  build: (m) => {
    limparPartida();
    desenharConstrucao(m);
    mostrar('construcao');
  },

  start: (m) => {
    S.snaps = [];
    S.local = null;
    S.baseViva = true;
    S.mapa = m.mapName;
    S.emJogo = true;
    S.pausado = false;
    S.nivel = 0;
    S.bonus = []; S.minas = []; S.torres = [];
    S.modo = m.modo || 'campanha';
    S.hud = {
      left: m.inimigos || 0, queue: 0, base: 1, stage: m.stage, total: m.totalFases,
      boss: null, baseHp: 1, baseHpMax: 1,
    };
    R.setTiles(m.tiles);
    R.reset();
    S.voltou = !!m.voltou;
    el('hud-mapa').textContent = m.mapName;
    el('hud-fase').textContent = `${m.stage + 1}/${m.totalFases}`;
    el('chefe-barra').classList.add('hidden');
    el('pausa').classList.add('hidden');
    prepararHabilidade();
    mostrar('jogo');
    ajustarEscala();
    // Voltar de uma queda não é fase nova: nada de faixa nem de fanfarra.
    if (m.voltou) {
      toast('De volta à partida.');
    } else {
      faixaDeFase(m);
      A.tocar('faseNova');
    }
  },

  snap: (m) => {
    if (m.dt) R.patchTiles(m.dt);
    if (m.fx) { R.spawnFx(m.fx); A.fx(m.fx); }
    // efeitos de duração: só avisam quando ligam, não a cada retrato
    if (m.hud.freeze > 0 && !(S.hud.freeze > 0)) A.tocar('congelar');
    if (m.hud.pa > 0 && !(S.hud.pa > 0)) A.tocar('pa');
    S.hud = m.hud;
    S.baseViva = !!m.hud.base;
    S.bonus = m.bn || [];
    S.minas = m.mn || [];
    S.torres = m.tr || [];

    S.snaps.push({ time: performance.now(), tanks: m.tanks, bullets: m.b });
    if (S.snaps.length > 14) S.snaps.shift();

    reconciliar(m.tanks);
    atualizarHud(m.tanks);
  },

  pausa: (m) => {
    S.pausado = !!m.on;
    el('pausa').classList.toggle('hidden', !S.pausado);
    A.tocar('pausa');
  },

  inter: (m) => {
    limparPartida();
    desenharIntervalo(m);
    mostrar('intervalo');
    A.tocar('vitoria');
  },

  interup: (m) => desenharEscolhas(m.escolhas),

  end: (m) => {
    limparPartida();
    desenharFim(m);
    mostrar('fim');
    A.tocar(m.result === 'vitoria' ? 'vitoria' : 'derrota');
  },

  close: () => {
    limparPartida();
    S.sala = null;
    esquecerVaga();
    avisarReconexao(null);
    el('menu-erro').textContent = 'Conexão perdida. Recarregue a página.';
    mostrar('menu');
  },
});

// ------------------------------------------------------------------ menu

el('nome').value = S.nome;
el('menu-recorde').textContent = lerRecorde().toLocaleString('pt-BR');
const paramSala = new URLSearchParams(location.search).get('sala');
if (paramSala) el('codigo').value = paramSala.toUpperCase();

function guardarNome() {
  S.nome = el('nome').value.trim();
  localStorage.setItem('apollo-nome', S.nome);
}

el('btn-criar').onclick = () => {
  guardarNome();
  el('menu-erro').textContent = '';
  net.send({ t: 'create', name: S.nome, cls: S.classe, skin: S.skin });
};

el('btn-entrar').onclick = () => {
  guardarNome();
  const code = el('codigo').value.trim().toUpperCase();
  if (code.length !== 4) { el('menu-erro').textContent = 'O código tem 4 caracteres.'; return; }
  el('menu-erro').textContent = '';
  net.send({ t: 'join', code, name: S.nome, cls: S.classe, skin: S.skin });
};

el('codigo').addEventListener('keydown', (e) => { if (e.key === 'Enter') el('btn-entrar').click(); });
el('nome').addEventListener('keydown', (e) => { if (e.key === 'Enter') el('btn-criar').click(); });

el('btn-perfil').onclick = () => { S.voltaPara = 'menu'; desenharPerfil(); mostrar('perfil'); };
el('btn-manual').onclick = () => { S.voltaPara = 'menu'; desenharManual('classes'); mostrar('manual'); };

// Som: dois botões (menu e HUD) e a tecla M, todos no mesmo estado.
function pintarBotoesSom() {
  for (const id of ['btn-som', 'btn-som-jogo']) {
    const b = el(id);
    b.textContent = A.ligado ? '🔊 Som' : '🔇 Mudo';
    b.setAttribute('aria-pressed', String(A.ligado));
  }
}
function alternarSom() {
  A.iniciar();
  A.alternar();
  pintarBotoesSom();
}
el('btn-som').onclick = alternarSom;
el('btn-som-jogo').onclick = alternarSom;
pintarBotoesSom();

// ------------------------------------------------------------------ perfil

// Miniatura do tanque com a skin, pro perfil. 24px de canvas mostrados em 48
// pela folha de estilo: dobro exato, sem borrar.
function miniTanque(skin, cor) {
  const c = document.createElement('canvas');
  c.width = 24; c.height = 24;
  const g = c.getContext('2d');
  g.imageSmoothingEnabled = false;
  g.translate(12, 13);
  pintarTanque(g, { cls: 'assalto', color: cor, skin, moving: false }, 0);
  return c.toDataURL();
}

function desenharPerfil() {
  el('perfil-nome').value = S.nome;
  el('perfil-skins').innerHTML = SKINS.map((s) => `
    <button class="skin ${s.id === S.skin ? 'on' : ''}" data-skin="${s.id}">
      <img src="${miniTanque(s.id, '#f2c14e')}" width="32" height="32" alt="">
      <span><b>${s.name}</b><span>${s.desc}</span></span>
    </button>`).join('');
  for (const b of el('perfil-skins').querySelectorAll('[data-skin]')) {
    b.onclick = () => {
      S.skin = b.dataset.skin;
      localStorage.setItem('apollo-skin', S.skin);
      net.send({ t: 'skin', skin: S.skin });
      desenharPerfil();
    };
  }

  const f = lerFicha();
  const linhas = [
    ['Recorde', lerRecorde()],
    ['Partidas', f.partidas],
    ['Campanhas vencidas', f.campanhas],
    ['Fases limpas', f.fases],
    ['Abates', f.abates],
    [`${MOEDA.nome} faturada`, f.sucata],
  ];
  el('perfil-ficha').innerHTML = linhas
    .map(([k, v]) => `<li><span>${k}</span><strong>${Number(v).toLocaleString('pt-BR')}</strong></li>`)
    .join('');
}

el('perfil-nome').addEventListener('input', () => {
  S.nome = el('perfil-nome').value.trim();
  localStorage.setItem('apollo-nome', S.nome);
  el('nome').value = S.nome;
});
el('btn-perfil-voltar').onclick = () => mostrar(S.voltaPara);
el('btn-perfil-zerar').onclick = () => {
  gravarFicha({ ...FICHA_VAZIA });
  localStorage.setItem('apollo-recorde', '0');
  el('menu-recorde').textContent = '0';
  desenharPerfil();
  toast('Ficha zerada.');
};

// ------------------------------------------------------------------ manual

function verbete(icone, titulo, texto) {
  return `<div class="verbete"><i>${icone}</i><span><b>${titulo}</b><span>${texto}</span></span></div>`;
}

function desenharManual(aba) {
  for (const b of el('manual-abas').querySelectorAll('[data-aba]')) {
    b.classList.toggle('on', b.dataset.aba === aba);
  }
  let html = '';
  if (aba === 'classes') {
    html = CLASS_IDS.map((id) => {
      const c = CLASSES[id], h = ABILITIES[c.ability];
      return verbete(c.icon, `${c.name} — ${h.icon} ${h.name}`,
        `${c.desc}<br><b style="color:var(--acento)">${h.name}:</b> ${h.desc}`);
    }).join('') + NIVEIS_TANQUE.map((n, i) => i === 0 ? '' :
      verbete('★', `Nível ${i} (estrela)`, `${n.desc}. O nível some quando você morre.`)).join('');
  } else if (aba === 'bonus') {
    html = BONUS_IDS.map((id) => verbete(BONUS[id].icon, BONUS[id].name, BONUS[id].desc)).join('')
      + verbete('!', 'Quem larga bônus',
        'Um em cada cinco inimigos entra piscando. É esse que solta o bônus quando cai — e ele vale 500 pontos a mais.');
  } else if (aba === 'terreno') {
    html = [
      ['▤', 'Tijolo', 'Cai com qualquer tiro. É do que o muro da águia é feito.'],
      ['▦', 'Aço', 'Só cede pra tiro perfurante: Pesado, Sniper, nível ★★★ ou o upgrade Núcleo.'],
      ['≈', 'Água', 'Barra o tanque, mas a bala passa por cima.'],
      ['♣', 'Mato', 'Todo mundo passa e some por baixo dele. Bom pra emboscar.'],
      ['❄', 'Gelo', 'O tanque não para na hora: escorrega mais um pouco depois que você solta.'],
    ].map(([i, t, d]) => verbete(i, t, d)).join('');
  } else {
    html = FORTIFICACOES.map((f) => verbete(f.icon, f.name,
      `${f.resumo}<br>${f.niveis.map((n, i) => `<b>${i + 1}.</b> ${n.desc} — ${MOEDA.simbolo} ${n.custo}`).join(' · ')}`)).join('')
      + verbete(MOEDA.simbolo, MOEDA.nome,
        'A moeda do esquadrão. Entra ao limpar cada fase e sai no Modo Construção, antes da fase seguinte.');
  }
  el('manual-corpo').innerHTML = html;
}

for (const b of el('manual-abas').querySelectorAll('[data-aba]')) {
  b.onclick = () => desenharManual(b.dataset.aba);
}
el('btn-manual-voltar').onclick = () => mostrar(S.voltaPara);

// ------------------------------------------------------------------ lobby

function desenharLobby(m) {
  el('lobby-codigo').textContent = m.code;
  el('lobby-contagem').textContent = `${m.players.length}/4`;

  el('lobby-jogadores').innerHTML = m.players
    .map((p) => `<li class="${p.offline ? 'caiu' : ''}"><span class="bolinha" style="background:${p.color}"></span>
      <span>${escapar(p.name)}</span>
      <span class="tag">${p.offline ? 'reconectando…'
        : `${CLASSES[p.cls]?.icon || ''} ${CLASSES[p.cls]?.name || p.cls}${p.id === m.host ? ' · host' : ''}`}</span></li>`)
    .join('');

  el('classes').innerHTML = CLASS_IDS.map((id) => {
    const c = CLASSES[id];
    const h = ABILITIES[c.ability];
    return `<button class="classe ${id === S.classe ? 'on' : ''}" data-cls="${id}">
      <b>${c.icon} ${c.name}</b><span>${c.desc}</span>
      <span><b style="color:var(--acento)">${h.icon} ${h.name}</b> — ${h.desc}</span></button>`;
  }).join('');
  for (const b of el('classes').querySelectorAll('[data-cls]')) {
    b.onclick = () => {
      S.classe = b.dataset.cls;
      localStorage.setItem('apollo-classe', S.classe);
      net.send({ t: 'cls', cls: S.classe });
    };
  }

  const campanha = (m.modo || 'campanha') === 'campanha';
  el('btn-campanha').classList.toggle('on', campanha);
  el('btn-treino').classList.toggle('on', !campanha);
  el('campanha-lista').classList.toggle('hidden', !campanha);
  el('mapas').classList.toggle('hidden', campanha);

  el('campanha-lista').innerHTML = (m.campanha || [])
    .map((f) => `<div class="fase-chip ${f.chefe ? 'chefe' : ''}">
      <b>${f.i + 1}. ${escapar(f.name)}</b><small>${escapar(f.tag)}</small></div>`)
    .join('');

  const souHost = m.host === S.meuId;
  el('host-area').classList.toggle('hidden', !souHost);
  el('aguardando').classList.toggle('hidden', souHost);
  el('mapas').innerHTML = m.maps
    .map((mp, i) => `<button data-stage="${i}" class="${i === m.stage ? 'on' : ''}">${mp.name}</button>`)
    .join('');
  for (const b of el('mapas').querySelectorAll('[data-stage]')) {
    b.onclick = () => net.send({ t: 'stage', stage: Number(b.dataset.stage) });
  }
}

el('btn-campanha').onclick = () => net.send({ t: 'modo', modo: 'campanha' });
el('btn-treino').onclick = () => net.send({ t: 'modo', modo: 'treino' });
el('btn-iniciar').onclick = () => net.send({ t: 'start' });
el('btn-denovo').onclick = () => net.send({ t: 'again' });
// Sair é de propósito: some com a vaga guardada, senão a aba tentaria voltar.
const sairDaSala = () => { esquecerVaga(); net.fechar(); location.href = location.pathname; };
el('btn-sair').onclick = sairDaSala;
el('btn-fim-sair').onclick = sairDaSala;
// Quem não é host não tem "Voltar pro lobby": sem isto a tela de fim é um
// beco sem saída e só o F5 resolve.
el('btn-lobby-manual').onclick = () => { S.voltaPara = 'lobby'; desenharManual('classes'); mostrar('manual'); };

el('btn-link').onclick = async () => {
  const url = `${location.origin}${location.pathname}?sala=${S.sala?.code || ''}`;
  try {
    await navigator.clipboard.writeText(url);
    toast('Link copiado — manda pro grupo!');
  } catch {
    prompt('Copie o link:', url);
  }
};

// ------------------------------------------------------------------ construção

let buildFim = 0;
let buildForts = {};

function desenharConstrucao(m) {
  buildFim = Date.now() + m.segundos * 1000;
  buildForts = Object.fromEntries(m.forts.map((f) => [f.id, f.nivel]));

  el('build-sub').textContent = m.chefe
    ? `Fase ${m.stage + 1} de ${m.totalFases} — ${m.mapName}. É lá que o Colosso espera: reforce a águia.`
    : `Fase ${m.stage + 1} de ${m.totalFases} — ${m.mapName} (${m.mapTag}). Gaste antes de sair.`;
  el('build-sucata').textContent = `${m.moeda.simbolo} ${m.sucata}`;

  el('build-cartas').innerHTML = m.forts.map((f) => {
    const noTeto = f.custo === null;
    const caro = !noTeto && f.custo > m.sucata;
    const proximo = noTeto ? 'no último nível' : f.niveis[f.nivel].desc;
    const bolinhas = Array.from({ length: f.max }, (_, i) => `<em class="${i < f.nivel ? 'on' : ''}"></em>`).join('');
    return `<button class="fort" data-fort="${f.id}" ${noTeto || caro ? 'disabled' : ''}>
      <span class="cabeca"><i>${f.icon}</i><b>${escapar(f.name)}</b><span class="niveis">${bolinhas}</span></span>
      <span>${escapar(f.resumo)}</span>
      <span class="preco ${caro ? 'caro' : ''}">${noTeto ? 'máximo' : `${m.moeda.simbolo} ${f.custo} · ${escapar(proximo)}`}</span>
    </button>`;
  }).join('');

  for (const b of el('build-cartas').querySelectorAll('[data-fort]')) {
    b.onclick = () => net.send({ t: 'comprar', id: b.dataset.fort });
  }

  el('build-prontos').innerHTML = m.prontos.map((p) => `<li class="${p.offline ? 'caiu' : ''}">
    <span class="bolinha" style="background:${p.color}"></span>
    <span>${escapar(p.name)}</span>
    <span class="tag">${p.offline ? 'reconectando…' : p.pronto ? 'pronto' : 'na obra…'}</span></li>`).join('');

  const eu = m.prontos.find((p) => p.id === S.meuId);
  el('btn-pronto').disabled = !!eu?.pronto;
  el('btn-pronto').textContent = eu?.pronto ? 'Esperando o esquadrão…' : 'Estou pronto';
  resgatarFoco();
  previaBase.draw(buildForts, performance.now());
}

el('btn-pronto').onclick = () => net.send({ t: 'pronto' });

// ------------------------------------------------------------------ intervalo

let interFim = 0;

function listaTipos(porTipo) {
  if (!porTipo?.length) return '<li><span>nenhum abate</span><strong>0</strong></li>';
  return porTipo.map((x) => `<li><span>${x.n}× ${escapar(x.tipo)}
    <small>(${PONTOS_INIMIGO[x.tipo] ?? 100} cada)</small></span>
    <strong>${x.pontos}</strong></li>`).join('');
}

function desenharIntervalo(m) {
  el('inter-titulo').textContent = `FASE ${m.stage + 1} LIMPA`;
  el('inter-sub').textContent = m.chefe
    ? `A seguir: ${m.mapName} — é lá que o Colosso espera.`
    : `A seguir: fase ${m.proxima + 1} de ${m.totalFases} — ${m.mapName} (${m.mapTag}).`;

  interFim = Date.now() + m.segundos * 1000;
  el('inter-tipos').innerHTML = listaTipos(m.porTipo);
  el('inter-titulo-conta').textContent = m.moeda.nome;
  el('inter-conta').innerHTML = (m.conta?.linhas || [])
    .map((l) => `<li><span>${escapar(l.rotulo)}</span><strong>+${l.valor}</strong></li>`)
    .join('') + `<li class="total"><span>Caixa do esquadrão</span>
      <strong>${m.moeda.simbolo} ${m.sucata}</strong></li>`;

  // a ficha do perfil acompanha o que aconteceu
  const f = lerFicha();
  f.fases += 1;
  f.sucata += m.conta?.total || 0;
  gravarFicha(f);

  el('inter-cartas').innerHTML = m.cartas
    .map((c) => `<button class="carta" data-up="${c.id}">
      <i>${c.icon}</i><b>${escapar(c.name)}</b><span>${escapar(c.desc)}</span></button>`)
    .join('');

  for (const b of el('inter-cartas').querySelectorAll('[data-up]')) {
    b.onclick = () => {
      net.send({ t: 'up', id: b.dataset.up });
      for (const outro of el('inter-cartas').querySelectorAll('[data-up]')) outro.disabled = true;
      b.classList.add('on');
      b.disabled = false;
      resgatarFoco();
    };
  }
  desenharEscolhas([]);
}

function desenharEscolhas(escolhas) {
  el('inter-escolhas').innerHTML = escolhas
    .map((e) => `<li class="${e.offline ? 'caiu' : ''}"><span class="bolinha" style="background:${e.color}"></span>
      <span>${escapar(e.name)}</span>
      <span class="tag">${e.offline ? 'reconectando…' : e.up ? escapar(e.up) : 'escolhendo…'}</span></li>`)
    .join('');
}

setInterval(() => {
  if (!telas.intervalo.classList.contains('hidden')) {
    const falta = Math.max(0, Math.ceil((interFim - Date.now()) / 1000));
    el('inter-relogio').textContent = `${falta}s`;
  }
  if (!telas.construcao.classList.contains('hidden')) {
    const falta = Math.max(0, Math.ceil((buildFim - Date.now()) / 1000));
    el('build-relogio').textContent = `${falta}s`;
    previaBase.draw(buildForts, performance.now());
  }
}, 250);

// ------------------------------------------------------------------ fim

function desenharFim(m) {
  const venceu = m.result === 'vitoria';
  el('fim-titulo').textContent = m.campanhaCompleta ? 'CAMPANHA VENCIDA'
    : venceu ? 'FASE LIMPA' : 'FIM DE JOGO';
  el('fim-titulo').style.color = venceu ? 'var(--ok)' : 'var(--perigo)';
  el('fim-sub').textContent = m.campanhaCompleta
    ? 'As 5 fases caíram e o Colosso foi junto. A águia está de pé.'
    : venceu
      ? 'Campo limpo.'
      : `A águia caiu ou o esquadrão foi eliminado — fase ${m.stage + 1} de ${m.totalFases}.`;

  el('fim-placar').innerHTML = m.scores
    .map((s) => `<li><span class="bolinha" style="background:${s.color}"></span>
      <span>${escapar(s.name)}</span>
      <span class="tag">${CLASSES[s.cls]?.name || ''} · ${s.kills} abates</span>
      <span class="pontos">${s.score}</span></li>`)
    .join('');
  el('fim-tipos').innerHTML = listaTipos(m.porTipo);

  // recorde e ficha ficam no navegador de quem jogou
  const meu = m.scores.find((s) => s.id === S.meuId) || m.scores[0];
  const f = lerFicha();
  f.partidas += 1;
  f.abates += meu?.kills || 0;
  f.pontos += meu?.score || 0;
  if (m.campanhaCompleta) f.campanhas += 1;
  gravarFicha(f);

  const recorde = lerRecorde();
  if ((meu?.score || 0) > recorde) {
    localStorage.setItem('apollo-recorde', String(meu.score));
    el('fim-recorde').textContent = `Recorde novo: ${meu.score.toLocaleString('pt-BR')} pontos.`;
  } else {
    el('fim-recorde').textContent = `Recorde: ${recorde.toLocaleString('pt-BR')} pontos.`;
  }
  el('menu-recorde').textContent = lerRecorde().toLocaleString('pt-BR');

  const souHost = S.sala?.host === S.meuId;
  el('btn-denovo').classList.toggle('hidden', !souHost);
  el('fim-aviso').classList.toggle('hidden', souHost);
}

// ------------------------------------------------------------------ entrada

const TECLAS = {
  ArrowUp: 0, KeyW: 0,
  ArrowRight: 1, KeyD: 1,
  ArrowDown: 2, KeyS: 2,
  ArrowLeft: 3, KeyA: 3,
};

const pilha = [];      // direções seguradas, a última manda
let atirando = false;
let ultimoEnvio = { d: null, m: false, f: false };
let ultimoEnvioT = 0;

// O teclado do jogo só existe DENTRO do jogo. Fora dele, engolir Espaço e as
// setas quebraria o resto: Espaço não aciona mais botão, seta não rola a
// página, e as telas longas (manual, construção, intervalo) ficam presas.
const emCampo = () => !telas.jogo.classList.contains('hidden');

// M liga e desliga o som em qualquer tela — menos digitando num campo.
addEventListener('keydown', (e) => {
  if (e.repeat || e.target instanceof HTMLInputElement) return;
  if (e.code === 'KeyM') { alternarSom(); e.preventDefault(); }
});

addEventListener('keydown', (e) => {
  if (e.repeat || !emCampo()) return;
  if (e.target instanceof HTMLInputElement) return;
  const d = TECLAS[e.code];
  if (d !== undefined) { if (!pilha.includes(d)) pilha.push(d); e.preventDefault(); }
  if (e.code === 'Space' || e.code === 'KeyJ') { atirando = true; e.preventDefault(); }
  if (e.code === 'ShiftLeft' || e.code === 'ShiftRight' || e.code === 'KeyE' || e.code === 'KeyK') {
    usarHabilidade(); e.preventDefault();
  }
  if (e.code === 'KeyP' && S.emJogo) { net.send({ t: 'pausa', on: !S.pausado }); e.preventDefault(); }
});

addEventListener('keyup', (e) => {
  const d = TECLAS[e.code];
  if (d !== undefined) { const i = pilha.indexOf(d); if (i >= 0) pilha.splice(i, 1); }
  if (e.code === 'Space' || e.code === 'KeyJ') atirando = false;
});

// Ao sair da aba o navegador congela o requestAnimationFrame — e com ele o
// envio de comandos. Sem isso o servidor continuaria com o último comando e o
// tanque sairia andando sozinho. Então limpamos e avisamos na hora.
function soltarTudo() {
  pilha.length = 0;
  atirando = false;
  ultimoEnvio = { d: null, m: false, f: false };
  net.send({ t: 'in', d: null, m: false, f: false });
}
addEventListener('blur', soltarTudo);
addEventListener('visibilitychange', () => { if (document.hidden) soltarTudo(); });

// controles de toque
if (matchMedia('(pointer: coarse)').matches) el('mobile').classList.remove('hidden');
for (const b of el('mobile').querySelectorAll('[data-dir]')) {
  const d = Number(b.dataset.dir);
  const on = (e) => { e.preventDefault(); if (!pilha.includes(d)) pilha.push(d); };
  const off = (e) => { e.preventDefault(); const i = pilha.indexOf(d); if (i >= 0) pilha.splice(i, 1); };
  b.addEventListener('pointerdown', on);
  b.addEventListener('pointerup', off);
  b.addEventListener('pointerleave', off);
  b.addEventListener('pointercancel', off);
}
el('btn-fogo').addEventListener('pointerdown', (e) => { e.preventDefault(); atirando = true; });
el('btn-fogo').addEventListener('pointerup', (e) => { e.preventDefault(); atirando = false; });
el('btn-hab').addEventListener('pointerdown', (e) => { e.preventDefault(); usarHabilidade(); });
el('hud-hab').addEventListener('click', () => usarHabilidade());

// A recarga quem manda é o servidor; aqui a gente só evita mandar à toa e
// adianta o efeito do Arranque pra não sentir o ping.
function usarHabilidade() {
  if (!S.emJogo || S.hab.cd > 0) return;
  net.send({ t: 'hab' });
  S.hab.cd = S.hab.max;
  if (S.hab.id === 'arranque' && S.local) S.local.dashT = DASH_TICKS;
}

function entradaAtual() {
  return { dir: pilha.length ? pilha[pilha.length - 1] : null, move: pilha.length > 0, fire: atirando };
}

function enviarEntrada(now) {
  const i = entradaAtual();
  const mudou = i.dir !== ultimoEnvio.d || i.move !== ultimoEnvio.m || i.fire !== ultimoEnvio.f;
  if (!mudou && now - ultimoEnvioT < 100) return;
  ultimoEnvio = { d: i.dir, m: i.move, f: i.fire };
  ultimoEnvioT = now;
  net.send({ t: 'in', d: i.dir, m: i.move, f: i.fire });
}

// ------------------------------------------------------------------ previsão

function outrosTanques() {
  const s = S.snaps[S.snaps.length - 1];
  if (!s) return [];
  return s.tanks
    .filter((t) => t.i !== S.meuTanque && t.a)
    .map((t) => ({ id: t.i, x: t.x, y: t.y, alive: true }));
}

function passoLocal() {
  if (!S.local || !S.local.alive || S.pausado) return;
  if (S.local.dashT > 0) S.local.dashT--;
  moveTank(S.local, entradaAtual(), R.tiles, outrosTanques());
}

// O servidor manda a verdade; se a diferença for pequena a gente puxa devagar
// pra não dar solavanco, e só teleporta quando é grande (respawn, empurrão).
function reconciliar(tanks) {
  const srv = tanks.find((t) => t.i === S.meuTanque);
  if (!srv) return;

  if (srv.ac !== undefined) { S.hab.cd = srv.ac; S.hab.max = srv.am || 1; }
  const nivelNovo = srv.lv || 0;
  if (nivelNovo > S.nivel) A.tocar('nivel');
  S.nivel = nivelNovo;

  if (!S.local) {
    const c = CLASSES[S.classe] || CLASSES.assalto;
    S.local = {
      id: srv.i, x: srv.x, y: srv.y, dir: srv.d, speed: srv.sp ?? c.speed,
      alive: !!srv.a, moving: false, tread: 0, dashT: srv.ab || 0, slide: 0,
    };
    return;
  }

  // A velocidade vem do servidor, já com os upgrades da campanha. Prever com
  // a velocidade de fábrica da classe daria uma deriva logo abaixo do limiar
  // de teleporte — ou seja, tanque borrachudo o tempo todo a partir da fase 2.
  if (srv.sp) S.local.speed = srv.sp;
  S.local.alive = !!srv.a;
  // O Arranque vale enquanto o servidor disser que vale.
  if (srv.ab) S.local.dashT = srv.ab;
  else if (!srv.ab && S.local.dashT > 0 && srv.a) S.local.dashT = Math.min(S.local.dashT, 3);

  const dist = Math.hypot(srv.x - S.local.x, srv.y - S.local.y);
  if (!srv.a || dist > 14) {
    S.local.x = srv.x; S.local.y = srv.y; S.local.dir = srv.d;
  } else {
    S.local.x += (srv.x - S.local.x) * 0.22;
    S.local.y += (srv.y - S.local.y) * 0.22;
  }
}

// ------------------------------------------------------------------ interpolação

function estadoInterpolado(agora) {
  const alvo = agora - INTERP_MS;
  const snaps = S.snaps;
  if (!snaps.length) return { tanks: [], bullets: [] };

  let a = snaps[0], b = snaps[snaps.length - 1];
  for (let i = 0; i < snaps.length - 1; i++) {
    if (snaps[i].time <= alvo && snaps[i + 1].time >= alvo) { a = snaps[i]; b = snaps[i + 1]; break; }
  }
  if (alvo >= snaps[snaps.length - 1].time) a = b = snaps[snaps.length - 1];

  const span = b.time - a.time;
  const k = span > 0 ? Math.max(0, Math.min(1, (alvo - a.time) / span)) : 1;

  const antes = new Map(a.tanks.map((t) => [t.i, t]));
  const tanks = b.tanks.map((t) => {
    const p = antes.get(t.i);
    return {
      id: t.i,
      x: p ? p.x + (t.x - p.x) * k : t.x,
      y: p ? p.y + (t.y - p.y) * k : t.y,
      dir: t.d, moving: !!t.m, alive: !!t.a, hp: t.h, hpMax: t.hm, shield: !!t.s,
      color: t.c, name: t.n, player: !!t.p, lives: t.l, score: t.sc, kills: t.kl,
      etype: t.e, cls: t.e, skin: t.sk || 'liso', boss: !!t.bs, portador: !!t.pt,
      dash: !!t.ab, pierce: !!t.pi, level: t.lv || 0,
    };
  });

  const antesB = new Map(a.bullets.map((x) => [x.i, x]));
  const bullets = b.bullets.map((x) => {
    const p = antesB.get(x.i);
    return { x: p ? p.x + (x.x - p.x) * k : x.x, y: p ? p.y + (x.y - p.y) * k : x.y, p: x.p, d: x.d, pi: x.pi };
  });

  // meu tanque usa a posição prevista localmente (sem lag de entrada)
  if (S.local) {
    const meu = tanks.find((t) => t.id === S.meuTanque);
    if (meu && meu.alive) {
      meu.x = S.local.x; meu.y = S.local.y; meu.dir = S.local.dir; meu.moving = S.local.moving;
      meu.dash = S.local.dashT > 0;
    }
  }
  return { tanks, bullets };
}

// ------------------------------------------------------------------ HUD

function prepararHabilidade() {
  const cls = CLASSES[S.classe] || CLASSES.assalto;
  const h = ABILITIES[cls.ability];
  S.hab = { id: cls.ability, cd: 0, max: h.cd };
  el('hab-icone').textContent = h.icon;
  el('hab-nome').textContent = h.name;
  el('hab-tecla').textContent = 'Shift';
}

function faixaDeFase(m) {
  const f = el('faixa');
  el('faixa-titulo').textContent = `FASE ${m.stage + 1} — ${m.mapName}`;
  el('faixa-sub').textContent = m.chefe ? 'o Colosso espera' : (m.mapTag || '');
  f.classList.remove('hidden');
  S.fimFaixa = performance.now() + 2400;
}

let hudT = 0;
function atualizarHud(tanks) {
  const agora = performance.now();
  if (agora - hudT < 200) return;
  hudT = agora;

  // O número vale mais que os quadradinhos: eles são cortados em 20 e a fase 4
  // tem 28 inimigos. Assim a informação não depende só da contagem visual.
  const restantes = Math.max(0, S.hud.left);
  el('hud-inimigos-n').textContent = restantes;
  el('hud-inimigos').innerHTML = '<i></i>'.repeat(Math.min(restantes, 20));
  el('hud-fase').textContent = `${(S.hud.stage ?? 0) + 1}/${S.hud.total ?? 1}`;

  el('hud-times').innerHTML = tanks
    .filter((t) => t.p)
    .map((t) => `<li class="${t.a || t.l > 0 ? '' : 'morto'}">
      <span class="bolinha" style="background:${t.c}"></span>
      <span>${escapar(t.n)}</span>
      <span class="vidas">♥${Math.max(0, t.l)} · ${t.sc}</span></li>`)
    .join('');

  el('hud-ping').textContent = net.ping ? `${net.ping} ms` : '—';

  // recarga da habilidade: a cortina sobe conforme o tempo que falta
  const frac = S.hab.max ? Math.max(0, Math.min(1, S.hab.cd / S.hab.max)) : 0;
  el('hab-recarga').style.height = `${frac * 100}%`;
  el('hud-hab').classList.toggle('pronta', S.hab.cd <= 0);

  el('hud-nivel').textContent = NIVEIS_TANQUE[S.nivel]?.nome || 'padrão';

  // efeitos que estão pegando agora
  const efeitos = [];
  if (S.hud.freeze > 0) efeitos.push(`${BONUS.relogio.icon} ${Math.ceil(S.hud.freeze / 60)}s`);
  if (S.hud.pa > 0) efeitos.push(`${BONUS.pa.icon} ${Math.ceil(S.hud.pa / 60)}s`);
  const eu = tanks.find((t) => t.i === S.meuTanque);
  if (eu?.s) efeitos.push(`${BONUS.capacete.icon}`);
  el('hud-efeitos').innerHTML = efeitos.map((e) => `<i>${e}</i>`).join('');

  // blindagem que sobrou na águia
  const vidaAguia = Math.max(0, S.hud.baseHp || 0);
  el('hud-aguia-n').textContent = vidaAguia;
  el('hud-aguia').innerHTML = '<i></i>'.repeat(vidaAguia);

  const chefe = S.hud.boss;
  el('chefe-barra').classList.toggle('hidden', !chefe);
  if (chefe) el('chefe-vida').style.width = `${Math.max(0, (chefe.hp / chefe.max) * 100)}%`;
}

// ------------------------------------------------------------------ laço

function ajustarEscala() {
  const largura = innerWidth - (innerWidth > 720 ? 200 : 32);
  const altura = innerHeight - 140;
  // Escala inteira pra não borrar o pixel art. Piso 1, não 2: num celular de
  // 360px de largura, o dobro já estoura a tela e a partida ganha rolagem
  // horizontal justamente pra quem joga no toque.
  const escala = Math.max(1, Math.min(5, Math.floor(Math.min(largura, altura) / FIELD)));
  canvas.style.width = `${FIELD * escala}px`;
  canvas.style.height = `${FIELD * escala}px`;
}
addEventListener('resize', ajustarEscala);
ajustarEscala();

let acumulado = 0;
let anterior = performance.now();

function frame(agora) {
  requestAnimationFrame(frame);
  const dt = Math.min(120, agora - anterior);
  anterior = agora;

  if (!S.emJogo) return;

  enviarEntrada(agora);

  if (S.fimFaixa && agora > S.fimFaixa) {
    S.fimFaixa = 0;
    el('faixa').classList.add('hidden');
  }

  acumulado += dt;
  let passos = 0;
  while (acumulado >= TICK_MS && passos < 8) { acumulado -= TICK_MS; passoLocal(); passos++; }
  if (passos === 8) acumulado = 0;

  const vista = estadoInterpolado(agora);
  R.draw({
    tanks: vista.tanks, bullets: vista.bullets, myId: S.meuTanque, time: agora,
    baseAlive: S.baseViva, bonus: S.bonus, minas: S.minas, torres: S.torres,
    pa: S.hud.pa || 0, freeze: S.hud.freeze || 0,
    baseHp: S.hud.baseHp ?? 1, baseHpMax: S.hud.baseHpMax ?? 1,
  });
}
requestAnimationFrame(frame);

// Estado exposto pro console do navegador e pros testes automatizados.
window.APOLLO = S;
// A conexão vai junto: no console dá pra inspecionar o ping, e o teste de
// navegador derruba o socket na mão pra exercitar a volta de uma queda.
window.APOLLO.net = net;

// ------------------------------------------------------------------ util

function escapar(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

mostrar('menu');
