// Pont WebRTC (PeerJS) pour le multijoueur navigateur.
// Topologie en étoile : l'hôte relaie les messages d'état ("S|...") directement en JS,
// pour que le relais continue même si la boucle Unity de l'hôte ralentit.
var PeerNetLib = {
  $PN: {
    peer: null,
    conns: {},
    isHost: false,
    obj: 'NetworkManager',

    emit: function (method, arg) {
      SendMessage(PN.obj, method, arg);
    },

    reset: function () {
      for (var k in PN.conns) { try { PN.conns[k].close(); } catch (e) {} }
      PN.conns = {};
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

    setup: function (conn) {
      conn.on('open', function () {
        PN.conns[conn.peer] = conn;
        PN.emit('OnPeerConnected', conn.peer);
      });
      conn.on('data', function (data) {
        if (typeof data !== 'string') return;
        if (PN.isHost && data.charAt(0) === 'S') {
          for (var k in PN.conns) {
            if (k !== conn.peer && PN.conns[k].open) PN.conns[k].send(data);
          }
        }
        PN.emit('OnNetData', conn.peer + '\n' + data);
      });
      conn.on('close', function () {
        if (PN.conns[conn.peer] === conn) {
          delete PN.conns[conn.peer];
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

  PeerNet_Broadcast: function (msgPtr) {
    var msg = UTF8ToString(msgPtr);
    for (var k in PN.conns) { if (PN.conns[k].open) PN.conns[k].send(msg); }
  },

  PeerNet_Leave: function () {
    PN.reset();
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
