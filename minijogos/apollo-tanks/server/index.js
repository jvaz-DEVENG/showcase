// Servidor: entrega os arquivos do cliente e cuida das salas por WebSocket.

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { WebSocketServer } from 'ws';

import { Room, makeCode } from './room.js';
import { CLASS_IDS, SKIN_IDS, MAX_PLAYERS, RECONECTA_MS } from '../shared/constants.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PORT = Number(process.env.PORT) || 8080;

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
};

// --------------------------------------------------------------- HTTP

const server = http.createServer((req, res) => {
  let urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
  if (urlPath === '/' || urlPath === '/index.html') urlPath = '/client/index.html';

  const target = path.join(ROOT, urlPath);
  const allowed = target.startsWith(path.join(ROOT, 'client')) || target.startsWith(path.join(ROOT, 'shared'));
  if (!allowed) { res.writeHead(404).end('nao encontrado'); return; }

  fs.readFile(target, (err, data) => {
    if (err) { res.writeHead(404).end('nao encontrado'); return; }
    res.writeHead(200, { 'content-type': MIME[path.extname(target)] || 'application/octet-stream', 'cache-control': 'no-cache' });
    res.end(data);
  });
});

// --------------------------------------------------------------- WebSocket

// maxPayload: o jogo nunca manda mais que alguns KB. Sem teto, o `ws` aceita
// ~100 MB por mensagem e um cliente qualquer come a memória da máquina.
const wss = new WebSocketServer({ server, path: '/ws', maxPayload: 64 * 1024 });
const MSG_POR_SEGUNDO = 120;      // o cliente manda ~20 comandos/s; o resto é abuso
const rooms = new Map();          // code -> Room
let nextClientId = 1;

const send = (ws, obj) => { if (ws.readyState === 1) ws.send(JSON.stringify(obj)); };
const fail = (ws, msg) => send(ws, { t: 'err', msg });

function leave(ws) {
  const room = ws.room;
  if (!room) return;
  room.removePlayer(ws.pid);
  ws.room = null;
  if (room.empty) {
    room.stop();
    rooms.delete(room.code);
  } else {
    room.sendLobby();
  }
}

wss.on('connection', (ws) => {
  ws.pid = `c${nextClientId++}`;
  ws.room = null;
  ws.isAlive = true;
  ws.janela = 0;
  ws.msgs = 0;
  ws.on('pong', () => { ws.isAlive = true; });

  send(ws, { t: 'hello', id: ws.pid, maxPlayers: MAX_PLAYERS, reconecta: RECONECTA_MS });

  ws.on('message', (raw) => {
    // Freio de mão: um cliente sozinho não pode ocupar o laço de eventos e
    // travar a partida dos outros.
    const agora = Date.now();
    if (agora - ws.janela > 1000) { ws.janela = agora; ws.msgs = 0; }
    if (++ws.msgs > MSG_POR_SEGUNDO) return;

    let m;
    try { m = JSON.parse(raw); } catch { return; }
    if (!m || typeof m.t !== 'string') return;

    // Nada que chegue da rede pode derrubar o processo: uma partida inteira
    // acabaria por causa de um cliente com mensagem torta.
    try {
      tratar(ws, m);
    } catch (e) {
      console.error(`[erro] mensagem "${m.t}" de ${ws.pid}:`, e.message);
      fail(ws, 'Não consegui processar isso.');
    }
  });

  ws.on('close', () => leave(ws));
  ws.on('error', () => leave(ws));
});

function tratar(ws, m) {
  {
    switch (m.t) {
      case 'create': {
        leave(ws);
        const code = makeCode(rooms);
        const room = new Room(code);
        rooms.set(code, room);
        const r = room.addPlayer(ws.pid, ws, m.name, m.cls, m.skin);
        if (!r.ok) { rooms.delete(code); return fail(ws, r.msg); }
        ws.room = room;
        send(ws, { t: 'vaga', code, token: r.player.token, id: ws.pid });
        room.sendLobby();
        break;
      }

      // Voltar pra vaga guardada depois de uma queda de conexão.
      case 'voltar': {
        const code = String(m.code || '').toUpperCase().trim();
        const room = rooms.get(code);
        if (!room) return fail(ws, 'A sala não existe mais.');
        leave(ws);
        const p = room.reconectar(String(m.token || ''), ws, ws.pid);
        if (!p) return fail(ws, 'A sua vaga não está mais guardada.');
        ws.room = room;
        // O cracha é privado: com ele qualquer um tomaria a vaga do outro.
        send(ws, { t: 'vaga', code: room.code, token: p.token, id: ws.pid });
        room.sendLobby();
        room.reenviarEstado(p);
        break;
      }

      case 'join': {
        const code = String(m.code || '').toUpperCase().trim();
        const room = rooms.get(code);
        if (!room) return fail(ws, `Sala ${code || '????'} não existe.`);
        // Entrar na sala em que já se está não faz nada. Sem esta guarda o
        // `leave()` abaixo apagaria a própria sala do mapa quando o jogador
        // estivesse sozinho nela — e o código vem pré-preenchido pelo ?sala=.
        if (ws.room === room) return;
        // Só sai da sala atual depois de saber que a nova aceita. Antes, um
        // "sala cheia" deixava o jogador sem sala nenhuma e sem aviso.
        if (!room.podeEntrar()) return fail(ws, room.motivoRecusa());
        leave(ws);
        const r = room.addPlayer(ws.pid, ws, m.name, m.cls, m.skin);
        if (!r.ok) return fail(ws, r.msg);
        ws.room = room;
        send(ws, { t: 'vaga', code, token: r.player.token, id: ws.pid });
        room.sendLobby();
        break;
      }

      case 'cls': {
        const room = ws.room;
        const p = room?.players.get(ws.pid);
        if (!p || room.state !== 'lobby') return;
        if (CLASS_IDS.includes(m.cls)) p.cls = m.cls;
        room.sendLobby();
        break;
      }

      // Skin é do perfil: pode trocar no lobby, entra no próximo tanque.
      case 'skin': {
        const room = ws.room;
        const p = room?.players.get(ws.pid);
        if (!p || room.state !== 'lobby') return;
        if (SKIN_IDS.includes(m.skin)) p.skin = m.skin;
        room.sendLobby();
        break;
      }

      // Escolher um mapa solto liga o modo treino: uma fase só, sem campanha.
      case 'stage': {
        const room = ws.room;
        if (!room || room.hostId !== ws.pid || room.state !== 'lobby') return;
        room.mode = 'treino';
        room.stage = Number(m.stage) || 0;
        room.sendLobby();
        break;
      }

      case 'modo': {
        const room = ws.room;
        if (!room || room.hostId !== ws.pid || room.state !== 'lobby') return;
        room.mode = m.modo === 'treino' ? 'treino' : 'campanha';
        if (room.mode === 'campanha') room.stage = 0;
        room.sendLobby();
        break;
      }

      case 'start': {
        const room = ws.room;
        if (!room || room.hostId !== ws.pid || !room.podeIniciar()) return;
        room.begin();
        break;
      }

      // Escolha do upgrade no intervalo entre fases.
      case 'up': {
        const room = ws.room;
        if (!room) return;
        room.escolherUpgrade(ws.pid, String(m.id || ''));
        break;
      }

      // Modo Construção: comprar defesa pra águia e avisar que terminou.
      case 'comprar': {
        const room = ws.room;
        if (!room) return;
        const r = room.comprarFort(ws.pid, String(m.id || ''));
        if (!r.ok) fail(ws, r.msg);
        break;
      }

      case 'pronto': {
        const room = ws.room;
        if (!room) return;
        room.prontoConstrucao(ws.pid);
        break;
      }

      case 'pausa': {
        const room = ws.room;
        if (!room) return;
        room.pausar(ws.pid, m.on);
        break;
      }

      case 'again': {
        const room = ws.room;
        if (!room || room.hostId !== ws.pid) return;
        room.backToLobby();
        break;
      }

      case 'ping':
        send(ws, { t: 'pong', ts: m.ts });
        break;

      case 'in': {
        const room = ws.room;
        const p = room?.players.get(ws.pid);
        if (!p || room.state !== 'playing') return;
        p.input = {
          dir: m.d === null || m.d === undefined ? null : (m.d | 0) & 3,
          move: !!m.m,
          fire: !!m.f,
        };
        break;
      }

      // Habilidade ativa: um toque, não um estado. O tick seguinte consome.
      case 'hab': {
        const room = ws.room;
        const p = room?.players.get(ws.pid);
        if (!p || room.state !== 'playing') return;
        p.usarHab = true;
        break;
      }
    }
  }
}

// Derruba conexão morta pra sala não ficar com fantasma.
setInterval(() => {
  for (const ws of wss.clients) {
    if (!ws.isAlive) { ws.terminate(); continue; }
    ws.isAlive = false;
    ws.ping();
  }
}, 20000);

// Limpa salas abandonadas.
setInterval(() => {
  for (const [code, room] of rooms) {
    if (room.empty && Date.now() - room.lastTouch > 60000) { room.stop(); rooms.delete(code); }
  }
}, 30000);

// --------------------------------------------------------------- start

server.listen(PORT, () => {
  const ips = ['localhost'];
  for (const list of Object.values(os.networkInterfaces())) {
    for (const net of list || []) if (net.family === 'IPv4' && !net.internal) ips.push(net.address);
  }
  console.log('');
  console.log('  APOLLO TANKS no ar');
  for (const ip of ips) console.log(`    http://${ip}:${PORT}`);
  console.log('');
  console.log('  Manda o endereço com o IP da rede pros seus amigos da mesma wi-fi.');
  console.log('  Ctrl+C pra parar.');
  console.log('');
});
