// Camada de rede: um WebSocket com callbacks por tipo de mensagem.
//
// A conexão se refaz sozinha. Uma queda de Wi-Fi de alguns segundos não pode
// custar a campanha inteira: o servidor guarda a vaga por um minuto e aqui a
// gente insiste em voltar, com espera crescente pra não martelar o servidor.

const MAX_TENTATIVAS = 12;

export function connect(handlers) {
  const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const url = `${proto}//${location.host}/ws`;

  let ws = null;
  let tentativas = 0;
  let desistiu = false;
  let saindo = false;         // fechamos de propósito: não tenta voltar

  const api = {
    ws: null,
    ping: 0,
    ready: false,
    get reconectando() { return !api.ready && !desistiu && !saindo; },
    send(obj) { if (ws && ws.readyState === 1) ws.send(JSON.stringify(obj)); },
    fechar() { saindo = true; try { ws?.close(); } catch {} },
  };

  function abrir() {
    ws = new WebSocket(url);
    api.ws = ws;

    ws.addEventListener('open', () => {
      api.ready = true;
      const voltando = tentativas > 0;
      tentativas = 0;
      handlers.open?.(voltando);
    });

    ws.addEventListener('message', (ev) => {
      let m;
      try { m = JSON.parse(ev.data); } catch { return; }
      if (m.t === 'pong') { api.ping = Math.round(performance.now() - m.ts); return; }
      handlers[m.t]?.(m);
    });

    ws.addEventListener('close', () => {
      api.ready = false;
      if (saindo || desistiu) return;
      if (tentativas < MAX_TENTATIVAS) {
        tentativas++;
        handlers.reconectando?.(tentativas, MAX_TENTATIVAS);
        setTimeout(abrir, Math.min(4000, 300 * tentativas));
      } else {
        desistiu = true;
        handlers.close?.();
      }
    });

    ws.addEventListener('error', () => handlers.error?.());
  }

  abrir();
  setInterval(() => api.send({ t: 'ping', ts: performance.now() }), 2000);
  return api;
}
