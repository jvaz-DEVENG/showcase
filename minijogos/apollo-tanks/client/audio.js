// Som do jogo, sintetizado na hora pelo WebAudio. Nenhum arquivo de áudio —
// mesma regra do desenho: tudo sai de código.
//
// Dois cuidados que mandam no formato daqui:
//   · o navegador só deixa tocar depois de um gesto do usuário, então o
//     contexto nasce suspenso e é destravado no primeiro clique ou tecla;
//   · os efeitos chegam em lote a 20 Hz e uma explosão múltipla viraria uma
//     pancada só de barulho, então há teto de vozes e de repetições por lote.

const MAX_VOZES = 14;

// Envelope curto e seco: nada aqui deve soar arrastado.
function envelope(ctx, ganho, vol, dur) {
  const t = ctx.currentTime;
  ganho.gain.setValueAtTime(0, t);
  ganho.gain.linearRampToValueAtTime(vol, t + 0.008);
  ganho.gain.exponentialRampToValueAtTime(0.0001, t + dur);
}

export function createAudio() {
  let ctx = null;
  let master = null;
  let ruidoBuf = null;
  let vozes = 0;
  let ligado = localStorage.getItem('apollo-som') !== 'off';
  let volume = Number(localStorage.getItem('apollo-volume') ?? 0.55);

  function iniciar() {
    if (ctx) { if (ctx.state === 'suspended') ctx.resume(); return; }
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;                       // navegador sem áudio: o jogo segue mudo
    ctx = new AC();
    master = ctx.createGain();
    master.gain.value = ligado ? volume : 0;
    master.connect(ctx.destination);

    // um segundo de ruído branco reaproveitado por todos os estouros
    const n = ctx.sampleRate;
    ruidoBuf = ctx.createBuffer(1, n, n);
    const dados = ruidoBuf.getChannelData(0);
    for (let i = 0; i < n; i++) dados[i] = Math.random() * 2 - 1;
  }

  const pronto = () => ctx && ligado && vozes < MAX_VOZES;

  function contar(no, dur) {
    vozes++;
    no.onended = () => { vozes--; };
    setTimeout(() => { try { no.stop(); } catch {} }, (dur + 0.2) * 1000);
  }

  // Oscilador com varredura de frequência — a base de quase tudo.
  function tom({ onda = 'square', f0, f1 = f0, dur = 0.1, vol = 0.2, atraso = 0 }) {
    if (!pronto()) return;
    const t = ctx.currentTime + atraso;
    const osc = ctx.createOscillator();
    const g = ctx.createGain();
    osc.type = onda;
    osc.frequency.setValueAtTime(f0, t);
    if (f1 !== f0) osc.frequency.exponentialRampToValueAtTime(Math.max(20, f1), t + dur);
    g.gain.setValueAtTime(0, t);
    g.gain.linearRampToValueAtTime(vol, t + 0.008);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    osc.connect(g).connect(master);
    osc.start(t);
    osc.stop(t + dur + 0.02);
    contar(osc, dur + atraso);
  }

  // Ruído filtrado — tijolo quebrando, explosão, terra voando.
  function ruido({ dur = 0.2, vol = 0.2, corte0 = 2000, corte1 = 300, atraso = 0 }) {
    if (!pronto()) return;
    const t = ctx.currentTime + atraso;
    const src = ctx.createBufferSource();
    src.buffer = ruidoBuf;
    const filtro = ctx.createBiquadFilter();
    filtro.type = 'lowpass';
    filtro.frequency.setValueAtTime(corte0, t);
    filtro.frequency.exponentialRampToValueAtTime(Math.max(40, corte1), t + dur);
    const g = ctx.createGain();
    envelope(ctx, g, vol, dur);
    src.connect(filtro).connect(g).connect(master);
    src.start(t);
    src.stop(t + dur + 0.02);
    contar(src, dur + atraso);
  }

  const arpejo = (notas, passo = 0.07, onda = 'square', vol = 0.16) =>
    notas.forEach((f, i) => tom({ onda, f0: f, dur: 0.12, vol, atraso: i * passo }));

  // ------------------------------------------------------------- catálogo

  const SONS = {
    tiro: () => { tom({ onda: 'square', f0: 640, f1: 170, dur: 0.07, vol: 0.14 }); ruido({ dur: 0.05, vol: 0.05, corte0: 3000, corte1: 800 }); },
    tiroPerf: () => { tom({ onda: 'sawtooth', f0: 1300, f1: 260, dur: 0.14, vol: 0.14 }); },
    toque: () => ruido({ dur: 0.07, vol: 0.10, corte0: 2600, corte1: 500 }),
    metal: () => { tom({ onda: 'triangle', f0: 2100, f1: 1400, dur: 0.06, vol: 0.10 }); ruido({ dur: 0.05, vol: 0.05, corte0: 5000, corte1: 2000 }); },
    boom: () => { ruido({ dur: 0.34, vol: 0.24, corte0: 1100, corte1: 90 }); tom({ onda: 'sine', f0: 130, f1: 45, dur: 0.3, vol: 0.16 }); },
    boomGrande: () => { ruido({ dur: 0.75, vol: 0.32, corte0: 1400, corte1: 50 }); tom({ onda: 'sine', f0: 95, f1: 28, dur: 0.6, vol: 0.24 }); },
    spawn: () => tom({ onda: 'sine', f0: 180, f1: 820, dur: 0.24, vol: 0.12 }),
    hab: () => { tom({ onda: 'square', f0: 300, f1: 950, dur: 0.16, vol: 0.13 }); },
    bonusCai: () => arpejo([660, 880], 0.09, 'triangle', 0.12),
    bonusPego: () => arpejo([523, 659, 784, 1047], 0.06, 'square', 0.15),
    nivel: () => arpejo([784, 988, 1175], 0.07, 'triangle', 0.16),
    vida: () => arpejo([659, 784, 1047], 0.08, 'triangle', 0.15),
    barragem: () => { tom({ onda: 'sawtooth', f0: 220, f1: 55, dur: 0.35, vol: 0.18 }); ruido({ dur: 0.3, vol: 0.14, corte0: 900, corte1: 120 }); },
    chefe: () => { tom({ onda: 'sawtooth', f0: 110, f1: 55, dur: 0.9, vol: 0.22 }); ruido({ dur: 0.6, vol: 0.14, corte0: 600, corte1: 60 }); },
    // alarme de duas notas: a águia levou tiro e a blindagem segurou
    aguia: () => { tom({ onda: 'square', f0: 880, dur: 0.1, vol: 0.16 }); tom({ onda: 'square', f0: 660, dur: 0.14, vol: 0.16, atraso: 0.11 }); },
    congelar: () => { tom({ onda: 'sine', f0: 1400, f1: 400, dur: 0.5, vol: 0.12 }); },
    pa: () => { tom({ onda: 'triangle', f0: 300, f1: 900, dur: 0.3, vol: 0.12 }); },
    vitoria: () => arpejo([523, 659, 784, 1047, 1319], 0.12, 'square', 0.16),
    derrota: () => arpejo([440, 392, 330, 262], 0.16, 'sawtooth', 0.15),
    faseNova: () => arpejo([392, 523, 659], 0.1, 'square', 0.14),
    pausa: () => tom({ onda: 'square', f0: 440, f1: 220, dur: 0.12, vol: 0.12 }),
    clique: () => tom({ onda: 'square', f0: 520, dur: 0.04, vol: 0.07 }),
  };

  // Os efeitos que a simulação manda → som. Nem todo efeito faz barulho:
  // o rastro do Arranque, por exemplo, é só visual.
  const POR_FX = {
    shot: 'tiro', ping: 'toque', spark: 'metal', spawn: 'spawn', hab: 'hab',
    bonus: 'bonusCai', pegou: 'bonusPego', granada: 'boomGrande',
    barragem: 'barragem', chefe: 'chefe', vida: 'vida', aguia: 'aguia',
  };

  // Teto por lote: 20 tiros no mesmo retrato viram um estouro só de barulho.
  const MAX_POR_LOTE = { tiro: 3, toque: 3, metal: 3, boom: 3, spawn: 2 };

  return {
    iniciar,
    get ligado() { return ligado; },
    get volume() { return volume; },

    alternar() {
      ligado = !ligado;
      localStorage.setItem('apollo-som', ligado ? 'on' : 'off');
      if (ligado) iniciar();
      if (master) master.gain.value = ligado ? volume : 0;
      if (ligado) SONS.clique();
      return ligado;
    },

    setVolume(v) {
      volume = Math.max(0, Math.min(1, v));
      localStorage.setItem('apollo-volume', String(volume));
      if (master) master.gain.value = ligado ? volume : 0;
    },

    tocar(nome) {
      if (!ctx) return;
      SONS[nome]?.();
    },

    // Recebe o lote de efeitos do retrato e toca o que tem som.
    fx(lista) {
      if (!ctx || !ligado || !lista?.length) return;
      const contagem = {};
      for (const f of lista) {
        let nome = POR_FX[f.k];
        if (f.k === 'boom') nome = f.big ? 'boomGrande' : 'boom';
        if (!nome) continue;
        const teto = MAX_POR_LOTE[nome];
        contagem[nome] = (contagem[nome] || 0) + 1;
        if (teto && contagem[nome] > teto) continue;
        SONS[nome]();
      }
    },
  };
}
