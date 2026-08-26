/* Roda todas as suítes em sequência e devolve um resumo.
   node run.js            -> tudo (a maratona usa 20 fases)
   node run.js --completo -> maratona de 60 fases
   node run.js --offline  -> pula a suíte que depende de internet */
const { execFileSync } = require('child_process');
const path = require('path');

const completo = process.argv.includes('--completo');
const offline  = process.argv.includes('--offline');

const suites = [
  { arq: '1-basico.js',    nome: 'Fundamentos' },
  { arq: '2-blindagem.js', nome: 'Blindagem de rede' },
  { arq: '3-balanco.js',   nome: 'Balanceamento' },
  { arq: '4-maratona.js',  nome: `Maratona (${completo ? 60 : 20} fases)`,
    env: { FASES: completo ? '60' : '20' } },
  { arq: '5-online.js',    nome: 'Online real', rede: true }
];

const resultado = [];
for(const s of suites){
  if(offline && s.rede){ resultado.push([s.nome, 'PULADA']); continue; }
  process.stdout.write(`\n${'='.repeat(60)}\n>>> ${s.nome}\n${'='.repeat(60)}\n`);
  try{
    execFileSync(process.execPath, [path.join(__dirname, s.arq)], {
      stdio: 'inherit',
      env: Object.assign({}, process.env, s.env || {})
    });
    resultado.push([s.nome, 'OK']);
  }catch(e){
    resultado.push([s.nome, 'FALHOU']);
  }
}

console.log('\n' + '='.repeat(60));
console.log('RESUMO');
console.log('='.repeat(60));
for(const [nome, r] of resultado) console.log(`  ${r.padEnd(7)} ${nome}`);
const falhou = resultado.filter(r => r[1] === 'FALHOU').length;
console.log(falhou ? `\n${falhou} suíte(s) falharam` : '\nTUDO VERDE');
process.exit(falhou ? 1 : 0);
