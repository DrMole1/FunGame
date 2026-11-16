// Ctrl+V : le texte collé est envoyé à Unity (méthode OnPaste du GameObject nommé), qui l'insère dans le champ actif.
mergeInto(LibraryManager.library, {
  FunGame_ListenPaste: function (objPtr) {
    var obj = UTF8ToString(objPtr);
    if (window.funGamePasteListener) return;
    window.funGamePasteListener = function (e) {
      var data = e.clipboardData || window.clipboardData;
      var text = data ? data.getData('text') : '';
      if (text) SendMessage(obj, 'OnPaste', text);
    };
    document.addEventListener('paste', window.funGamePasteListener);
  },
});
