/* Fundamentos: o jogo sobe, o labirinto é jogável, os mobs andam e saem do
   ninho, as habilidades respondem à tecla certa. */
const { novaInstancia, novoPlacar } = require('./harness');
const P = novoPlacar('BASICO');
const A = novaInstancia();
const X = A.X;
const nx = x => ((x % X.grade()[0]) + X.grade()[0]) % X.grade()[0];

P.secao('1) partida sobe');
X.save.charLevel = 10;
X.novoJogo('campanha'); A.quadros(10);
P.ok('entrou em jogo', X.G.estado === 'jogando', X.G.estado);
P.ok('tem labirinto', !!X.G.grid && X.G.grid.length === X.grade()[1]);
P.ok('tem jogador', !!X.euJogador());
P.ok('tem núcleos', X.G.restantes > 100, X.G.restantes + ' núcleos');

P.secao('2) todo núcleo é alcançável a pé');
function alcancaveis(sx, sy){
  const [C, R] = X.grade();
  const vis = Array.from({length:R}, () => new Array(C).fill(false));
  const fila = [[sx, sy]]; vis[sy][sx] = true;
  while(fila.length){
    const [x, y] = fila.pop();
    for(const d of [[1,0],[-1,0],[0,1],[0,-1]]){
      const j = y + d[1]; if(j < 0 || j >= R) continue;
      const i = nx(x + d[0]);
      if(vis[j][i] || X.G.grid[j][i] !== 0) continue;
      vis[j][i] = true; fila.push([i, j]);
    }
  }
  return vis;
}
let presos = 0, total = 0;
{
  const p = X.euJogador();
  const vis = alcancaveis(nx(X.tileDe(p.x)), X.tileDe(p.y));
  const [C, R] = X.grade();
  for(let y = 0; y < R; y++) for(let x = 0; x < C; x++)
    if(X.G.pellets[y][x]){ total++; if(!vis[y][x]) presos++; }
}
P.ok('nenhum núcleo ilhado', presos === 0, `${presos} de ${total}`);

P.secao('3) mobs saem do ninho e não entram em parede');
X.G.players.forEach(p => p.invuln = 1e9);
let emParede = 0, foraDaGrade = 0;
const saiuDoNinho = new Set();
for(let t = 0; t < 60 * 25; t++){
  A.quadros(1);
  const [C, R] = X.grade();
  for(const m of X.G.mobs){
    const my = X.tileDe(m.y), mx = nx(X.tileDe(m.x));
    if(my < 0 || my >= R){ foraDaGrade++; continue; }
    if(m.saiu) saiuDoNinho.add(m);
    if(m.estado === 'olhos') continue;
    const centrado = Math.abs(m.x % X.TILE - X.TILE/2) < 2 &&
                     Math.abs(m.y % X.TILE - X.TILE/2) < 2;
    if(centrado && X.G.grid[my][mx] === 1) emParede++;
  }
}
P.ok('ninguém parado dentro de parede', emParede === 0, emParede + ' quadros');
P.ok('ninguém fora da grade no eixo Y', foraDaGrade === 0, foraDaGrade + ' quadros');
P.ok('todos saíram do ninho', saiuDoNinho.size === X.G.mobs.length,
     `${saiuDoNinho.size}/${X.G.mobs.length}`);

P.secao('4) teclas das habilidades');
{
  const p = X.euJogador();
  p.carga = 100; p.pulsoCd = 0; p.dashCd = 0; p.dir = X.DIRS.left;
  X.G.mobs.forEach(m => { m.stun = 0; m.imunePulso = 0; m.saida = 0; });
  const alvo = X.G.mobs[0];
  alvo.x = p.x + X.TILE; alvo.y = p.y; alvo.estado = 'normal';
  A.tecla('ShiftLeft');
  P.ok('SHIFT atordoa', alvo.stun > 0, 'stun=' + alvo.stun.toFixed(1));
  X.G.mobs.forEach(m => { m.stun = 0; m.imunePulso = 0; });
  p.carga = 100; p.pulsoCd = 0; p.dashCd = 0;
  A.tecla('Space');
  P.ok('ESPAÇO é dash, não pulso',
       X.G.mobs.every(m => m.stun <= 0) && p.dashCd > 0, 'dashCd=' + p.dashCd.toFixed(1));
}

P.secao('5) mob atordoado é atravessável');
{
  const p = X.euJogador();
  p.invuln = 0; p.escudo = 0; p.buffs = {}; p.morto = 0;
  const vidas = p.vidas;
  const m = X.G.mobs[0];
  m.stun = 2.6; m.saida = 0; m.estado = 'normal'; m.x = p.x; m.y = p.y;
  A.quadros(8);
  P.ok('atravessou sem perder vida', p.vidas === vidas && p.morto === 0,
       `${vidas} -> ${p.vidas}`);
}

P.fim();
