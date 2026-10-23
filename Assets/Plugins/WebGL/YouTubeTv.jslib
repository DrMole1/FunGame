// Vidéos YouTube des Télévisions (GDD 7.6, V0.4.7) et collage de texte dans les zones de saisie.
// Une vidéo YouTube ne peut pas être copiée dans une texture WebGL (iframe d'un autre domaine) : chaque Télévision
// reçoit un lecteur YouTube (IFrame API) posé par-dessus le canvas, déformé en perspective (matrix3d) sur les quatre
// coins de son écran projetés par Unity. Il ne capte ni la souris ni le clavier ; Unity le masque quand l'écran est
// caché (mur, hors champ, menus) et règle son volume selon la distance.
var YouTubeTvLib = {
  $YT_TV: {
    next: 1,
    tvs: {},
    apiState: 0, // 0 : non chargée, 1 : en chargement, 2 : prête
    waiting: [],
    gesture: null,

    loadApi: function (done) {
      if (YT_TV.apiState === 2) { done(); return; }
      YT_TV.waiting.push(done);
      if (YT_TV.apiState === 1) return;
      YT_TV.apiState = 1;
      var previous = window.onYouTubeIframeAPIReady;
      window.onYouTubeIframeAPIReady = function () {
        YT_TV.apiState = 2;
        if (typeof previous === 'function') { try { previous(); } catch (e) {} }
        var list = YT_TV.waiting;
        YT_TV.waiting = [];
        for (var i = 0; i < list.length; i++) { try { list[i](); } catch (e) {} }
      };
      var tag = document.createElement('script');
      tag.src = 'https://www.youtube.com/iframe_api';
      document.head.appendChild(tag);
    },

    // Lecture bloquée par le navigateur (aucun clic encore sur la page) : relancée au premier clic ou touche du joueur.
    listenGesture: function () {
      if (YT_TV.gesture) return;
      YT_TV.gesture = function () {
        for (var k in YT_TV.tvs) {
          var tv = YT_TV.tvs[k];
          if (tv.ready && tv.player && !tv.playing) { try { tv.player.unMute(); tv.player.setVolume(tv.volume); tv.player.playVideo(); } catch (e) {} }
        }
      };
      document.addEventListener('pointerdown', YT_TV.gesture, true);
      document.addEventListener('keydown', YT_TV.gesture, true);
    },

    // Matrice CSS qui envoie le rectangle (0,0)-(w,h) sur le quadrilatère p (8 valeurs, en pixels CSS).
    adj: function (m) {
      return [m[4] * m[8] - m[5] * m[7], m[2] * m[7] - m[1] * m[8], m[1] * m[5] - m[2] * m[4],
              m[5] * m[6] - m[3] * m[8], m[0] * m[8] - m[2] * m[6], m[2] * m[3] - m[0] * m[5],
              m[3] * m[7] - m[4] * m[6], m[1] * m[6] - m[0] * m[7], m[0] * m[4] - m[1] * m[3]];
    },
    mulMM: function (a, b) {
      var c = [];
      for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++) {
          var s = 0;
          for (var k = 0; k < 3; k++) s += a[3 * i + k] * b[3 * k + j];
          c[3 * i + j] = s;
        }
      return c;
    },
    mulMV: function (m, v) {
      return [m[0] * v[0] + m[1] * v[1] + m[2] * v[2], m[3] * v[0] + m[4] * v[1] + m[5] * v[2], m[6] * v[0] + m[7] * v[1] + m[8] * v[2]];
    },
    basis: function (p) {
      var m = [p[0], p[2], p[4], p[1], p[3], p[5], 1, 1, 1];
      var v = YT_TV.mulMV(YT_TV.adj(m), [p[6], p[7], 1]);
      return YT_TV.mulMM(m, [v[0], 0, 0, 0, v[1], 0, 0, 0, v[2]]);
    },
    projection: function (w, h, p) {
      var t = YT_TV.mulMM(YT_TV.basis(p), YT_TV.adj(YT_TV.basis([0, 0, w, 0, w, h, 0, h])));
      for (var i = 0; i < 9; i++) t[i] = t[i] / t[8];
      return 'matrix3d(' + [t[0], t[3], 0, t[6], t[1], t[4], 0, t[7], 0, 0, 1, 0, t[2], t[5], 0, t[8]].join(',') + ')';
    },
  },

  // Crée le lecteur (caché) de la vidéo 'videoId', lancée à 'start' secondes ; renvoie son numéro.
  YouTubeTv_Create: function (videoPtr, start) {
    var videoId = UTF8ToString(videoPtr);
    var id = YT_TV.next++;
    var W = 640, H = 480;
    var box = document.createElement('div');
    box.style.cssText = 'position:fixed;left:0;top:0;width:' + W + 'px;height:' + H + 'px;transform-origin:0 0;' +
      'pointer-events:none;visibility:hidden;z-index:5;background:#000;overflow:hidden;';
    var inner = document.createElement('div');
    inner.id = 'funGameTv' + id;
    box.appendChild(inner);
    document.body.appendChild(box);
    var tv = { box: box, player: null, volume: 0, w: W, h: H, ready: false, playing: false };
    YT_TV.tvs[id] = tv;
    YT_TV.listenGesture();
    YT_TV.loadApi(function () {
      if (!YT_TV.tvs[id]) return;
      // L'iframe est créée ici avec allow="autoplay" : sans cette délégation, le navigateur coupe le son (ou bloque la
      // lecture) chez les joueurs qui n'ont pas eux-mêmes collé le lien.
      var frame = document.createElement('iframe');
      frame.id = inner.id;
      frame.width = W;
      frame.height = H;
      frame.setAttribute('frameborder', '0');
      frame.setAttribute('allow', 'autoplay; encrypted-media');
      frame.style.pointerEvents = 'none';
      frame.src = 'https://www.youtube.com/embed/' + encodeURIComponent(videoId) + '?enablejsapi=1&autoplay=1&controls=0' +
        '&disablekb=1&fs=0&rel=0&playsinline=1&iv_load_policy=3&modestbranding=1&start=' + Math.max(0, Math.floor(start)) +
        '&origin=' + encodeURIComponent(window.location.origin);
      box.replaceChild(frame, inner);
      tv.player = new YT.Player(frame, {
        events: {
          onReady: function (e) {
            tv.ready = true;
            try { e.target.setVolume(tv.volume); e.target.unMute(); e.target.playVideo(); } catch (err) {}
          },
          onStateChange: function (e) { tv.playing = e.data === 1 || e.data === 0; }
        }
      });
    });
    return id;
  },

  // Coins de l'écran en coordonnées normalisées du canvas (origine en haut à gauche) : haut-gauche, haut-droit,
  // bas-droit, bas-gauche. Volume de 0 à 100.
  YouTubeTv_Update: function (id, visible, x0, y0, x1, y1, x2, y2, x3, y3, volume) {
    var tv = YT_TV.tvs[id];
    if (!tv) return;
    var canvas = document.querySelector('#unity-canvas') || document.querySelector('canvas');
    var r = canvas ? canvas.getBoundingClientRect() : { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
    if (visible) {
      var p = [r.left + x0 * r.width, r.top + y0 * r.height, r.left + x1 * r.width, r.top + y1 * r.height,
               r.left + x2 * r.width, r.top + y2 * r.height, r.left + x3 * r.width, r.top + y3 * r.height];
      tv.box.style.transform = YT_TV.projection(tv.w, tv.h, p);
    }
    tv.box.style.visibility = visible ? 'visible' : 'hidden';
    var v = Math.max(0, Math.min(100, Math.round(volume)));
    if (v !== tv.volume) {
      tv.volume = v;
      if (tv.ready && tv.player) { try { tv.player.setVolume(v); } catch (e) {} }
    }
  },

  YouTubeTv_Destroy: function (id) {
    var tv = YT_TV.tvs[id];
    if (!tv) return;
    delete YT_TV.tvs[id];
    try { if (tv.player) tv.player.destroy(); } catch (e) {}
    if (tv.box.parentNode) tv.box.parentNode.removeChild(tv.box);
  },

  // Ctrl+V : le texte collé est envoyé à Unity (méthode OnPaste du GameObject nommé), qui l'insère dans le champ actif.
  YouTubeTv_ListenPaste: function (objPtr) {
    var obj = UTF8ToString(objPtr);
    if (window.funGamePasteListener) return;
    window.funGamePasteListener = function (e) {
      var data = e.clipboardData || window.clipboardData;
      var text = data ? data.getData('text') : '';
      if (text) SendMessage(obj, 'OnPaste', text);
    };
    document.addEventListener('paste', window.funGamePasteListener);
  },
};

autoAddDeps(YouTubeTvLib, '$YT_TV');
mergeInto(LibraryManager.library, YouTubeTvLib);
