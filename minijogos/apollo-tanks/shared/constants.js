// Constantes compartilhadas entre servidor e cliente.
// Tudo em "unidades de campo": o tabuleiro tem 208x208 unidades, igual ao NES.

export const TILE = 8;                 // meio-tijolo — menor pedaço destrutível
export const GRID = 26;                // 26x26 células de 8u = 13x13 tijolos
export const FIELD = TILE * GRID;      // 208
export const TANK = 16;                // tanque ocupa 2x2 células
export const BULLET = 4;

export const TICK_RATE = 60;           // passos de simulação por segundo
export const TICK_MS = 1000 / TICK_RATE;
export const SNAPSHOT_EVERY = 3;       // manda estado a cada 3 ticks = 20 Hz
export const INTERP_MS = 110;          // atraso de interpolação no cliente

export const DIR = { UP: 0, RIGHT: 1, DOWN: 2, LEFT: 3 };
export const DIRV = [[0, -1], [1, 0], [0, 1], [-1, 0]];

export const T = {
  EMPTY: 0,
  BRICK: 1,
  STEEL: 2,
  WATER: 3,
  TREES: 4,
  BASE: 5,
  BASE_DEAD: 6,
  ICE: 7,        // escorrega: o tanque desliza um pouco depois de soltar
};

export const TEAM = { PLAYER: 0, ENEMY: 1 };

export const MAX_PLAYERS = 4;
export const PLAYER_LIVES = 3;
export const ENEMIES_PER_STAGE = 20;   // padrão; a campanha manda o número por fase
export const ENEMIES_ALIVE_MAX = 4;    // quantos ficam em campo ao mesmo tempo
export const ENEMY_SPAWN_EVERY = 150;  // ticks entre spawns (2,5s)
export const RESPAWN_TICKS = 90;       // 1,5s parado antes de voltar
export const SHIELD_TICKS = 180;       // 3s de escudo ao nascer

// Onde cada jogador nasce (canto inferior), em unidades de campo.
export const PLAYER_SPAWNS = [
  { x: 8 * TILE, y: 24 * TILE },
  { x: 16 * TILE, y: 24 * TILE },
  { x: 4 * TILE, y: 24 * TILE },
  { x: 20 * TILE, y: 24 * TILE },
];

// Inimigos entram pelo topo: esquerda, centro, direita.
export const ENEMY_SPAWNS = [
  { x: 0, y: 0 },
  { x: 12 * TILE, y: 0 },
  { x: 24 * TILE, y: 0 },
];

// As 4 cores de jogador. Elas são CLARAS de propósito, e as dos inimigos são
// de meio-tom: matiz colapsa no daltonismo, luminosidade nunca. Assim dá pra
// separar amigo de inimigo até em tons de cinza. Mexer aqui sem refazer a
// conta de distância perceptual reabre o problema — o azul do jogador 2 já foi
// igualzinho ao do inimigo "veloz".
export const PLAYER_COLORS = ['#f2c14e', '#4fbdff', '#c8ff70', '#ff7ad4'];

// ------------------------------------------------------------- habilidades

export const DASH_MULT = 2.6;          // quanto o Arranque multiplica a velocidade
export const DASH_TICKS = 22;

// Cada classe tem uma habilidade ativa com recarga própria (`cd` em ticks).
export const ABILITIES = {
  arranque: {
    name: 'Arranque', icon: '»', cd: 380,
    desc: 'Dispara pra frente por meio segundo. Pra fugir ou pra fechar distância.',
  },
  bastiao: {
    name: 'Bastião', icon: '▣', cd: 760,
    desc: 'Escudo que aguenta qualquer coisa por 3 segundos.',
  },
  perfurar: {
    name: 'Perfurar', icon: '✦', cd: 540,
    desc: 'O próximo tiro atravessa parede, aço e tanque sem parar.',
  },
  reparo: {
    name: 'Reparo', icon: '✚', cd: 800,
    desc: 'Refaz o muro da águia na hora e dá escudo a quem estiver perto.',
  },
};

// As 4 classes.
export const CLASSES = {
  assalto: {
    name: 'Assalto',
    icon: '⚡',
    speed: 0.92,
    fireCd: 17,
    maxBullets: 2,
    bulletSpeed: 2.6,
    power: 1,
    hp: 1,
    ability: 'arranque',
    desc: 'Rápido e com cadência alta. Bom pra caçar e fugir.',
  },
  pesado: {
    name: 'Pesado',
    icon: '🛡',
    speed: 0.56,
    fireCd: 34,
    maxBullets: 1,
    bulletSpeed: 2.2,
    power: 2,
    hp: 2,
    ability: 'bastiao',
    desc: 'Lento, aguenta 2 tiros e o tiro atravessa aço.',
  },
  sniper: {
    name: 'Sniper',
    icon: '🎯',
    speed: 0.74,
    fireCd: 42,
    maxBullets: 1,
    bulletSpeed: 4.6,
    power: 2,
    hp: 1,
    ability: 'perfurar',
    desc: 'Tiro muito veloz que perfura aço. Recarga longa.',
  },
  suporte: {
    name: 'Suporte',
    icon: '🔧',
    speed: 0.80,
    fireCd: 24,
    maxBullets: 2,
    bulletSpeed: 2.4,
    power: 1,
    hp: 1,
    ability: 'reparo',
    desc: 'Reconstrói o muro da base e volta mais rápido ao morrer.',
  },
};

export const CLASS_IDS = Object.keys(CLASSES);

// ------------------------------------------------------------- progressão

// Escolhido no intervalo entre as fases e mantido até o fim da campanha.
// `apply` só mexe no tanque recebido — é chamado toda vez que o tanque nasce.
export const UPGRADES = [
  {
    id: 'motor', name: 'Motor turbinado', icon: '»',
    desc: '+18% de velocidade',
    apply: (t) => { t.speed *= 1.18; },
  },
  {
    id: 'gatilho', name: 'Gatilho leve', icon: '⚡',
    desc: '−20% de recarga do tiro',
    apply: (t) => { t.fireCdMax = Math.max(6, Math.round(t.fireCdMax * 0.8)); },
  },
  {
    id: 'pente', name: 'Pente extra', icon: '⋯',
    desc: '+1 bala sua na tela',
    apply: (t) => { t.maxBullets += 1; },
  },
  {
    id: 'polvora', name: 'Pólvora densa', icon: '↗',
    desc: '+35% na velocidade do tiro',
    apply: (t) => { t.bulletSpeed *= 1.35; },
  },
  {
    id: 'blindagem', name: 'Blindagem', icon: '▣',
    desc: '+1 tiro aguentado',
    apply: (t) => { t.hpMax += 1; t.hp = t.hpMax; },
  },
  {
    id: 'nucleo', name: 'Núcleo perfurante', icon: '✦',
    desc: 'seu tiro passa a rasgar aço',
    apply: (t) => { t.power = Math.max(t.power, 2); },
  },
  {
    id: 'reator', name: 'Reator frio', icon: '↻',
    desc: '−30% na recarga da habilidade',
    apply: (t) => { t.abilityCdMax = Math.max(60, Math.round(t.abilityCdMax * 0.7)); },
  },
  // `umaVez` marca o upgrade que é um EVENTO, não um atributo do tanque: ele é
  // pago na hora da escolha e não pode ser reaplicado a cada nascimento, senão
  // renderia uma vida por fase em vez de uma só.
  {
    id: 'reserva', name: 'Tanque reserva', icon: '♥',
    desc: '+1 vida agora',
    umaVez: true,
    apply: (t) => { t.lives += 1; },
  },
];

export const UPGRADE_BY_ID = Object.fromEntries(UPGRADES.map((u) => [u.id, u]));

export const UPGRADE_CARDS = 3;        // quantas opções aparecem no intervalo
export const INTERMISSION_MS = 18000;  // tempo pra escolher antes de seguir sozinho
export const STAGE_BONUS_LIVES = 1;    // vida ganha ao limpar uma fase

// ------------------------------------------------- bônus (os do NES)

// No original, um em cada tantos inimigos vem piscando: ao morrer, larga um
// bônus no campo. São os mesmos seis de sempre.
export const BONUS = {
  capacete: { name: 'Capacete', icon: '⛑', desc: 'Escudo por 10 segundos.' },
  relogio: { name: 'Relógio', icon: '⧗', desc: 'Congela todos os inimigos por 8 segundos.' },
  pa: { name: 'Pá', icon: '⛏', desc: 'O muro da águia vira aço por 20 segundos.' },
  estrela: { name: 'Estrela', icon: '★', desc: 'Sobe um nível o seu tanque.' },
  granada: { name: 'Granada', icon: '✹', desc: 'Explode todos os inimigos que estão em campo.' },
  tanque: { name: 'Tanque', icon: '♥', desc: 'Uma vida a mais.' },
};
export const BONUS_IDS = Object.keys(BONUS);

export const CHANCE_PORTADOR = 0.2;    // 1 em 5 inimigos vem com bônus
export const BONUS_PONTOS = 500;       // pegar qualquer bônus dá isso
export const BONUS_DURACAO = 900;      // 15s no chão antes de sumir
export const CAPACETE_TICKS = 600;
export const RELOGIO_TICKS = 480;
export const PA_TICKS = 1200;
export const PA_AVISO = 180;           // últimos 3s o aço fica piscando
export const GELO_DESLIZE = 20;        // ticks deslizando depois de soltar

// Nível do tanque pela estrela, igual ao original: some quando você morre.
export const NIVEIS_TANQUE = [
  { nome: 'padrão', desc: 'o tanque de fábrica da sua classe' },
  { nome: '★', desc: 'tiro 35% mais rápido' },
  { nome: '★★', desc: 'mais uma bala sua na tela' },
  { nome: '★★★', desc: 'o tiro passa a rasgar aço' },
];
export const NIVEL_MAX = 3;

// Pontos por tipo de inimigo, como no placar de fim de fase do NES.
export const PONTOS_INIMIGO = {
  basico: 100, veloz: 200, canhao: 300, blindado: 400, colosso: 2000,
};
export const VIDA_EXTRA_A_CADA = 20000;

// ------------------------------------------------- economia

export const MOEDA = { nome: 'Sucata', simbolo: '⛭' };

// Quanto o esquadrão fatura ao limpar uma fase.
export const SUCATA = {
  base: 40,
  porAbate: 8,
  aguiaIntacta: 60,      // nenhum tijolo do muro caiu
  semMortes: 40,         // ninguém perdeu vida
};

// ------------------------------------------------- fortificações da águia
//
// Compradas no Modo Construção e montadas no começo de cada fase. O que vale é
// o nível: cada compra sobe um degrau e o preço do próximo já vem na lista.
export const FORTIFICACOES = [
  {
    id: 'aco', name: 'Muralha de aço', icon: '▦',
    resumo: 'Troca o tijolo do muro por aço, que só cai pra tiro perfurante.',
    niveis: [
      { custo: 120, desc: 'um terço do muro nasce em aço' },
      { custo: 200, desc: 'dois terços em aço' },
      { custo: 320, desc: 'muro inteiro em aço' },
    ],
  },
  {
    id: 'blindagem', name: 'Blindagem da águia', icon: '✚',
    resumo: 'A águia passa a aguentar tiro em vez de cair no primeiro.',
    niveis: [
      { custo: 150, desc: 'aguenta 1 tiro antes de cair' },
      { custo: 260, desc: 'aguenta 2 tiros' },
      { custo: 400, desc: 'aguenta 3 tiros' },
    ],
  },
  {
    id: 'reparo', name: 'Auto-reparo', icon: '↻',
    resumo: 'O muro se refaz sozinho, sem precisar de um Suporte por perto.',
    niveis: [
      { custo: 140, desc: 'um tijolo a cada 8 segundos' },
      { custo: 240, desc: 'um tijolo a cada 4 segundos' },
    ],
  },
  {
    id: 'minas', name: 'Campo de minas', icon: '◈',
    resumo: 'Minas em volta da águia. A primeira coisa inimiga que passa por cima vai pelos ares.',
    niveis: [
      { custo: 100, desc: '2 minas' },
      { custo: 180, desc: '4 minas' },
      { custo: 280, desc: '6 minas' },
    ],
  },
  {
    id: 'sentinela', name: 'Sentinela', icon: '⌖',
    resumo: 'Torre fixa ao lado da águia que atira sozinha em quem entrar na linha.',
    niveis: [
      { custo: 220, desc: '1 torre' },
      { custo: 380, desc: '2 torres' },
    ],
  },
];
export const FORT_BY_ID = Object.fromEntries(FORTIFICACOES.map((f) => [f.id, f]));

export const SENTINELA = { hp: 3, fireCd: 70, bulletSpeed: 2.6, alcance: 96 };
export const CONSTRUCAO_MS = 45000;    // tempo máximo no Modo Construção
export const RECONECTA_MS = 60000;     // quanto tempo a vaga espera quem caiu

// ------------------------------------------------- perfil

// A cor continua sendo do slot (é ela que diz quem é quem no meio do tiroteio);
// a skin muda só o padrão pintado no casco.
export const SKINS = [
  { id: 'liso', name: 'Liso', desc: 'sem pintura, direto ao ponto' },
  { id: 'listras', name: 'Listras', desc: 'duas faixas no casco' },
  { id: 'xadrez', name: 'Xadrez', desc: 'quadriculado de corrida' },
  { id: 'camuflado', name: 'Camuflado', desc: 'manchas irregulares' },
  { id: 'tigre', name: 'Tigre', desc: 'riscos atravessados' },
  { id: 'veterano', name: 'Veterano', desc: 'friso claro na borda e marca de abate' },
];
export const SKIN_IDS = SKINS.map((s) => s.id);

// A campanha: 5 fases em sequência, cada uma mais cheia que a anterior.
// `tier` puxa os inimigos mais fortes; a última tem o chefe.
export const CAMPAIGN = [
  { map: 0, enemies: 16, alive: 4, every: 145, tier: 0 },
  { map: 1, enemies: 20, alive: 4, every: 130, tier: 1 },
  { map: 2, enemies: 24, alive: 5, every: 115, tier: 2 },
  { map: 3, enemies: 28, alive: 5, every: 105, tier: 3 },
  { map: 4, enemies: 18, alive: 5, every: 110, tier: 3, boss: true },
];

// O chefe da última fase. Anda como qualquer um, mas aguenta muito e solta
// uma barragem pros 4 lados de vez em quando.
export const BOSS = {
  id: 'colosso',
  name: 'COLOSSO',
  color: '#e5484d',
  speed: 0.52,
  fireCd: 26,
  maxBullets: 5,
  bulletSpeed: 2.9,
  power: 2,
  hp: 26,
  barrageEvery: 260,     // ticks entre barragens
};

// O chefe não morre de um golpe só: granada e mina tiram um naco, não a vida.
export const GRANADA_NO_CHEFE = 8;
export const MINA_NO_CHEFE = 4;
