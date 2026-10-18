// Pont WebRTC (PeerJS) pour le multijoueur navigateur.
// Topologie en étoile : l'hôte relaie les messages d'état ("S|...") directement en JS,
// pour que le relais continue même si la boucle Unity de l'hôte ralentit.
// Les positions (S, EN) passent par un second canal non fiable et non ordonné : un paquet perdu est
// remplacé par le suivant au lieu de bloquer tous les autres le temps d'être renvoyé.
var PeerNetLib = {
  $PN: {
    peer: null,
    conns: {},
    fast: {},
    isHost: false,
    obj: 'NetworkManager',

    emit: function (method, arg) {
      SendMessage(PN.obj, method, arg);
    },

    reset: function () {
      for (var k in PN.conns) { try { PN.conns[k].close(); } catch (e) {} }
      PN.conns = {};
      PN.fast = {};
      if (PN.peer) { try { PN.peer.destroy(); } catch (e) {} }
      PN.peer = null;
      PN.isHost = false;
    },

    createPeer: function (id) {
      if (typeof Peer === 'undefined') {
        PN.emit('OnNetError', 'peerjs-missing');
        return null;
      }
      var peer = id ? new Peer(id, { debug: 1 }) : new Peer({ debug: 1 });
      peer.on('open', function (myId) { PN.emit('OnNetOpen', myId); });
      peer.on('error', function (err) { PN.emit('OnNetError', String(err.type || err)); });
      peer.on('disconnected', function () {
        // Perte du serveur de signalisation : les connexions existantes restent, on tente de se reconnecter.
        if (PN.peer === peer && !peer.destroyed) { try { peer.reconnect(); } catch (e) {} }
      });
      return peer;
    },

    // Reçu d'un pair (canal fiable ou rapide) ; l'hôte relaie aussitôt les états des joueurs aux autres.
    receive: function (from, data) {
      if (typeof data !== 'string') return;
      if (PN.isHost && data.charAt(0) === 'S' && data.charAt(1) === '|') {
        for (var k in PN.conns) {
          if (k !== from) PN.sendFast(k, data);
        }
      }
      PN.emit('OnNetData', from + '\n' + data);
    },

    // Canal négocié des deux côtés avec le même identifiant : aucune signalisation supplémentaire.
    openFast: function (conn) {
      var pc = conn.peerConnection;
      if (!pc) return;
      try {
        var ch = pc.createDataChannel('fast', { negotiated: true, id: 100, ordered: false, maxRetransmits: 0 });
        ch.onopen = function () { if (PN.conns[conn.peer] === conn) PN.fast[conn.peer] = ch; };
        ch.onclose = function () { if (PN.fast[conn.peer] === ch) delete PN.fast[conn.peer]; };
        ch.onmessage = function (e) { if (PN.conns[conn.peer] === conn) PN.receive(conn.peer, e.data); };
      } catch (e) {
        console.warn('PeerNet fast channel unavailable', e);
      }
    },

    // Envoi non fiable si le canal rapide est ouvert, sinon par la connexion fiable.
    sendFast: function (to, msg) {
      var ch = PN.fast[to];
      if (ch && ch.readyState === 'open') {
        try { ch.send(msg); return; } catch (e) {}
      }
      var c = PN.conns[to];
      if (c && c.open) c.send(msg);
    },

    setup: function (conn) {
      conn.on('open', function () {
        PN.conns[conn.peer] = conn;
        PN.openFast(conn);
        PN.emit('OnPeerConnected', conn.peer);
      });
      conn.on('data', function (data) { PN.receive(conn.peer, data); });
      conn.on('close', function () {
        if (PN.conns[conn.peer] === conn) {
          delete PN.conns[conn.peer];
          delete PN.fast[conn.peer];
          PN.emit('OnPeerDisconnected', conn.peer);
        }
      });
      conn.on('error', function (err) { console.warn('PeerNet connection error', err); });
    }
  },

  PeerNet_Host: function (idPtr) {
    var id = UTF8ToString(idPtr);
    PN.reset();
    PN.isHost = true;
    var peer = PN.createPeer(id);
    if (!peer) return;
    PN.peer = peer;
    peer.on('connection', function (conn) { PN.setup(conn); });
  },

  PeerNet_Join: function (hostIdPtr) {
    var hostId = UTF8ToString(hostIdPtr);
    PN.reset();
    var peer = PN.createPeer(null);
    if (!peer) return;
    PN.peer = peer;
    peer.on('open', function () {
      var conn = peer.connect(hostId, { reliable: true, serialization: 'json' });
      PN.setup(conn);
    });
  },

  PeerNet_Send: function (toPtr, msgPtr) {
    var c = PN.conns[UTF8ToString(toPtr)];
    if (c && c.open) c.send(UTF8ToString(msgPtr));
  },

  PeerNet_SendFast: function (toPtr, msgPtr) {
    PN.sendFast(UTF8ToString(toPtr), UTF8ToString(msgPtr));
  },

  PeerNet_BroadcastFast: function (msgPtr) {
    var msg = UTF8ToString(msgPtr);
    for (var k in PN.conns) PN.sendFast(k, msg);
  },

  PeerNet_Broadcast: function (msgPtr) {
    var msg = UTF8ToString(msgPtr);
    for (var k in PN.conns) { if (PN.conns[k].open) PN.conns[k].send(msg); }
  },

  PeerNet_Leave: function () {
    PN.reset();
  },

  PeerNet_Kick: function (idPtr) {
    var id = UTF8ToString(idPtr);
    var c = PN.conns[id];
    if (!c) return;
    // On retire la connexion tout de suite (pas de second OnPeerDisconnected) et on la ferme
    // après un court délai pour laisser partir le message d'expulsion.
    delete PN.conns[id];
    delete PN.fast[id];
    setTimeout(function () { try { c.close(); } catch (e) {} }, 400);
  },

  PeerNet_TimezoneOffset: function () {
    return new Date().getTimezoneOffset();
  },

  PeerNet_CopyText: function (textPtr) {
    var text = UTF8ToString(textPtr);
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).catch(function () { window.prompt('Copiez ce lien :', text); });
    } else {
      window.prompt('Copiez ce lien :', text);
    }
  }
};

autoAddDeps(PeerNetLib, '$PN');
mergeInto(LibraryManager.library, PeerNetLib);
