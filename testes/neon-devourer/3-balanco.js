/* Balanceamento com NÚMERO, não com opinião: curva de dificuldade, uptime das
   habilidades e a linha de visão do ímã. */
const { novaInstancia, novoPlacar } = require('./harness');
const P = novoPlacar('BALANCO');
const A = novaInstancia();
const X = A.X;
X.save.charLevel = 10;
X.novoJogo('campanha'); A.quadros(10);

const VEL_JOGADOR_BASE = 5.6;
const VEL_JOGADOR_MAX  = 5.6 * 1.2;   // Velocidade 5

P.secao('1) a dificuldade ainda sobe depois da fase 10?');
const linha = f => {
  X.G.fase = f;
  const tipos = X.tiposDaFase(f);
  let vel = 0, nome = '';
  tipos.forEach((t, i) => {
    const m = X.criarMob(t, i, false);
    if(m.velBase > vel){ vel = m.velBase; nome = t; }
  });
  return { f, n: tipos.length, vel, ag: X.agressao(), nome };
};
const amostras = [1, 4, 9, 10, 11, 14, 20, 28, 40].map(linha);
P.info('fase | mobs | mais rápido        | agressão');
amostras.forEach(r => P.info(
  `${String(r.f).padStart(4)} | ${String(r.n).padStart(4)} | ` +
  `${r.vel.toFixed(2)} (${r.nome.padEnd(10)}) | ${r.ag.toFixed(2)}`));

const f10 = amostras.find(r => r.f === 10);
const f11 = amostras.find(r => r.f === 11);
const f28 = amostras.find(r => r.f === 28);
const f40 = amostras.find(r => r.f === 40);
P.ok('fase 11 é mais difícil que a 10', f11.vel > f10.vel || f11.ag > f10.ag,
     `vel ${f10.vel.toFixed(2)}->${f11.vel.toFixed(2)} ag ${f10.ag.toFixed(2)}->${f11.ag.toFixed(2)}`);
P.ok('fase 28 bem acima da 10', f28.vel > f10.vel + 0.15 && f28.ag >= 0.99,
     `vel ${f10.vel.toFixed(2)} -> ${f28.vel.toFixed(2)}`);
P.ok('nunca passa do jogador com Velocidade 5', f40.vel < VEL_JOGADOR_MAX,
     `mob ${f40.vel.toFixed(2)} < ${VEL_JOGADOR_MAX.toFixed(2)}`);
P.ok('fase 1 continua acessível', amostras[0].vel < VEL_JOGADOR_BASE,
     amostras[0].vel.toFixed(2));

P.secao('2) uptime das habilidades');
const rendaCarga = VEL_JOGADOR_BASE * 2;      // 2 de carga por núcleo, ~1 núcleo por tile
const a = X.attr();
P.info(`renda de carga andando: ${rendaCarga.toFixed(1)}/s`);
const uptime = (dur, cd, custo) => dur / Math.max(cd, custo / rendaCarga);
const uFase  = uptime(2.2, 10, X.CUSTO_FASE);
const uPulso = uptime(2.6, 1.2 + 5.6, a.pulsoCusto);   // 5.6 = carência por mob
P.ok('Modo Fase abaixo de 25% do tempo', uFase < 0.25, (uFase * 100).toFixed(0) + '%');
P.ok('Pulso não trava o mapa', uPulso < 0.5, (uPulso * 100).toFixed(0) + '% por mob');

P.secao('3) Modo Fase deixou de ser imunidade a tudo');
{
  const fonte = require('./harness').corpoDoJogo();
  P.ok('laser ignora faseT', !/p\.invuln > 0 \|\| p\.faseT > 0/.test(fonte));
  P.ok('tiro ignora faseT', !/p\.morto > 0 \|\| p\.invuln > 0 \|\| p\.faseT > 0/.test(fonte));
}

P.secao('4) raio do pulso');
{
  X.save.up = { pulso: 5 };
  const a5 = X.attr();
  const [C, R] = X.grade();
  const fracao = (Math.PI * a5.pulsoR * a5.pulsoR) / (C * R);
  P.info(`raio máximo ${a5.pulsoR.toFixed(1)} tiles — ${(fracao * 100).toFixed(0)}% do mapa`);
  P.ok('cobre menos de 20% do mapa', fracao < 0.20, (fracao * 100).toFixed(0) + '%');
  X.save.up = {};
}

P.secao('5) carência impede stun-lock');
{
  X.novoJogo('campanha'); A.quadros(5);
  const p = X.euJogador();
  const m = X.G.mobs[0];
  m.x = p.x + X.TILE; m.y = p.y; m.saida = 0; m.estado = 'normal';
  m.stun = 0; m.imunePulso = 0;
  X.pulsoOnda(p, X.TILE * 6, true);
  const primeiro = m.stun;
  m.stun = 0;                                  // finge que o stun acabou agora
  X.pulsoOnda(p, X.TILE * 6, true);
  P.ok('2o pulso seguido não re-atordoa', m.stun === 0,
       `1o=${primeiro.toFixed(1)}s carência=${m.imunePulso.toFixed(1)}s`);
}

P.secao('6) ímã não atravessa parede');
{
  X.novoJogo('campanha'); A.quadros(5);
  const p = X.euJogador();
  const [C, R] = X.grade();
  const nx = v => ((v % C) + C) % C;
  const tx = nx(X.tileDe(p.x)), ty = X.tileDe(p.y);
  const campo = X.campoColeta(tx, ty, 5);
  let paredes = 0, foraDoRaio = 0;
  for(const k of campo){
    const y = (k / C) | 0, x = k % C;
    if(X.G.grid[y][x] === 1) paredes++;
    let dx = Math.abs(x - tx); dx = Math.min(dx, C - dx);
    if(dx + Math.abs(y - ty) > 5) foraDoRaio++;
  }
  P.info(`campo alcançável: ${campo.size} tiles`);
  P.ok('nenhuma parede no campo', paredes === 0, paredes + ' parede(s)');
  P.ok('nada além do raio', foraDoRaio === 0, foraDoRaio + ' tile(s)');

  // prova prática: núcleo do outro lado de uma parede tem que sobreviver
  let caso = null;
  for(let y = 1; y < R - 1 && !caso; y++){
    for(let x = 1; x < C - 1; x++){
      if(X.G.grid[y][x] !== 1) continue;              // a parede
      if(x + 1 >= C - 1 || X.G.grid[y][x + 1] !== 0) continue;  // corredor do lado de lá
      if(X.G.grid[y][x - 1] !== 0) continue;          // corredor do lado de cá
      caso = { parede: {x, y}, alvo: {x: x + 1, y}, meu: {x: x - 1, y} };
      break;
    }
  }
  if(caso){
    p.x = (caso.meu.x + 0.5) * X.TILE;
    p.y = (caso.meu.y + 0.5) * X.TILE;
    p.imaChave = null;
    X.G.pellets[caso.alvo.y][caso.alvo.x] = 1;
    X.coletar(p, { ima: 3, devorar: 7 });
    P.ok('núcleo atrás da parede sobreviveu',
         X.G.pellets[caso.alvo.y][caso.alvo.x] === 1,
         `parede (${caso.parede.x},${caso.parede.y}) núcleo (${caso.alvo.x},${caso.alvo.y})`);
  }else{
    P.info('(nenhum par parede/corredor nesse mapa — sem o que provar)');
  }
}

P.secao('7) sentinela só atira com corredor livre');
{
  X.novoJogo('campanha'); A.quadros(5);
  const p = X.euJogador();
  const m = X.G.mobs[0];
  m.tipo = 'sentinela';
  const [C] = X.grade();
  const nx = v => ((v % C) + C) % C;
  let px = nx(X.tileDe(p.x)), py = X.tileDe(p.y), passos = 0;
  while(passos < 6 && X.G.grid[py][nx(px - 1)] === 0){ px = nx(px - 1); passos++; }
  if(passos >= 2){
    m.x = (px + 0.5) * X.TILE; m.y = (py + 0.5) * X.TILE;
    P.ok('atira com corredor livre', !!X.miraLivre(m), passos + ' tiles de corredor');
  }
  // agora um beco: sem linha nenhuma
  let beco = null;
  const [Cc, Rr] = X.grade();
  for(let y = 1; y < Rr - 1 && !beco; y++)
    for(let x = 1; x < Cc - 1; x++)
      if(X.G.grid[y][x] === 0 && X.G.grid[y][x-1] === 1 &&
         X.G.grid[y][x+1] === 1 && X.G.grid[y-1][x] === 1){ beco = {x, y}; break; }
  if(beco){
    m.x = (beco.x + 0.5) * X.TILE; m.y = (beco.y + 0.5) * X.TILE;
    p.x = 11; p.y = 11;
    P.ok('não atira sem linha', X.miraLivre(m) === null, `beco (${beco.x},${beco.y})`);
    P.ok('atirar() devolve false e não gasta a recarga', X.atirar(m) === false);
  }
}

P.fim();
