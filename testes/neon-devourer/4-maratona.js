/* MARATONA: joga 60 fases seguidas procurando o que só aparece no longo prazo.
   Verifica o que realmente importa:
     - todo núcleo alcançável a pé desde o spawn (mapa impossível = jogo travado)
     - ninho conectado ao labirinto (mob nascendo dentro de parede)
     - ninguém sai da grade no eixo Y (no X o túnel dá wrap: é legítimo)
     - nenhuma entidade ALINHADA dentro de parede (no meio do tile é trânsito)
     - nada crescendo sem limite
   Rode com FASES=20 no ambiente para uma versão curta. */
const { novaInstancia, novoPlacar } = require('./harness');

const FASES = Number(process.env.FASES || 60);
const SEGUNDOS_POR_FASE = Number(process.env.SEG || 45);

const P = novoPlacar(`MARATONA DE ${FASES} FASES`);
const A = novaInstancia();
const X = A.X;
const [C0] = X.grade();
const nx = v => { const [C] = X.grade(); return ((v % C) + C) % C; };

// tiles alcançáveis a pé desde um ponto, com wrap horizontal
function alcancaveis(sx, sy){
  const [C, R] = X.grade();
  const vis = Array.from({ length: R }, () => new Array(C).fill(false));
  if(!X.G.grid[sy] || X.G.grid[sy][sx] !== 0) return vis;
  const fila = [[sx, sy]];
  vis[sy][sx] = true;
  while(fila.length){
    const [x, y] = fila.pop();
    for(const d of [[1,0],[-1,0],[0,1],[0,-1]]){
      const j = y + d[1];
      if(j < 0 || j >= R) continue;
      const i = nx(x + d[0]);
      if(vis[j][i] || X.G.grid[j][i] !== 0) continue;
      vis[j][i] = true;
      fila.push([i, j]);
    }
  }
  return vis;
}

X.save.charLevel = 10;
X.novoJogo('campanha');
A.quadros(10);

let picoParts = 0, picoTiros = 0, picoMobs = 0;
const biomasVistos = {};
const problemas = [];

for(let f = 1; f <= FASES; f++){
  const p = X.euJogador();
  const bioma = X.G.bioma ? X.G.bioma.id : '?';
  const mod = (X.G.bioma && X.G.bioma.mod) || '-';
  biomasVistos[bioma] = (biomasVistos[bioma] || 0) + 1;

  // 1) todo núcleo alcançável?
  const vis = alcancaveis(nx(X.tileDe(p.x)), X.tileDe(p.y));
  const [C, R] = X.grade();
  let presos = 0, total = 0;
  for(let y = 0; y < R; y++)
    for(let x = 0; x < C; x++)
      if(X.G.pellets[y][x]){ total++; if(!vis[y][x]) presos++; }
  if(presos) problemas.push(`fase ${f} (${bioma}/${mod}): ${presos}/${total} núcleos inalcançáveis`);

  // 2) o ninho conversa com o labirinto?
  const ninhoOk = X.G.mobs.some(m => {
    const my = X.tileDe(m.y), mx = nx(X.tileDe(m.x));
    return my >= 0 && my < R && vis[my] && vis[my][mx];
  });
  if(!ninhoOk) problemas.push(`fase ${f} (${bioma}/${mod}): ninho isolado`);

  // 3) roda a fase vigiando invariantes
  let foraY = 0, emParede = 0;
  for(let t = 0; t < 60 * SEGUNDOS_POR_FASE && X.G.estado === 'jogando'; t++){
    if(t % 12 === 0){
      const tx = X.tileDe(p.x), ty = X.tileDe(p.y);
      const opc = ['left','right','up','down'].map(k => X.DIRS[k])
        .filter(d => X.livre(tx + d.x, ty + d.y, false));
      if(opc.length) p.quer = opc[(Math.random() * opc.length) | 0];
    }
    if(t % 150 === 0){ p.carga = 100; X.usarPulso(p); X.usarDash(p); }
    p.invuln = 1e9; p.vidas = 9; p.fora = false; p.morto = 0;
    A.quadros(1);

    picoParts = Math.max(picoParts, X.G.parts.length);
    picoTiros = Math.max(picoTiros, (X.G.tiros || []).length);
    picoMobs  = Math.max(picoMobs,  X.G.mobs.length);

    for(const e of [p].concat(X.G.mobs)){
      const ey = X.tileDe(e.y);
      if(ey < 0 || ey >= R){ foraY++; continue; }
      if(e.estado === 'olhos' || e.faseT > 0) continue;
      const centrado = Math.abs(e.x % X.TILE - X.TILE/2) < 2 &&
                       Math.abs(e.y % X.TILE - X.TILE/2) < 2;
      if(centrado && X.G.grid[ey][nx(X.tileDe(e.x))] === 1) emParede++;
    }
  }
  if(foraY)    problemas.push(`fase ${f}: ${foraY} quadros fora da grade em Y`);
  if(emParede) problemas.push(`fase ${f} (${bioma}/${mod}): ${emParede} quadros parado em parede`);

  console.log(`fase ${String(f).padStart(2)} | ${bioma.padEnd(9)} ${String(mod).padEnd(9)} | ` +
              `núcleos ${String(total).padStart(3)} presos ${presos} | mobs ${X.G.mobs.length} | ` +
              `ninho ${ninhoOk ? 'ok' : 'ISOLADO'}`);

  X.G.restantes = 0;
  A.quadros(30);
  if(X.G.estado !== 'upgrades'){ problemas.push(`fase ${f}: não concluiu (${X.G.estado})`); break; }
  X.proximaFase();
  A.quadros(30);
  if(X.G.estado !== 'jogando'){ problemas.push(`fase ${f}: não voltou a jogar (${X.G.estado})`); break; }
}

console.log('\nbiomas sorteados:', JSON.stringify(biomasVistos));
console.log(`picos: partículas=${picoParts} tiros=${picoTiros} mobs=${picoMobs}`);
console.log(`nível final=${X.save.charLevel} créditos=${X.save.coins} talentos=${X.save.pontos}`);

P.secao('resultado');
P.ok('nenhum problema estrutural em ' + FASES + ' fases', problemas.length === 0,
     problemas.length + ' problema(s)');
problemas.slice(0, 12).forEach(x => console.log('   · ' + x));
P.ok('partículas dentro do teto', picoParts <= 400, 'pico ' + picoParts);
P.fim();
