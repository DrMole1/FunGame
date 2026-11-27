using UnityEngine;

// Outil de vérification (éditeur, en Play) : déplace le joueur local, puis rend l'image de la caméra du jeu (effets compris) depuis
// une position et une direction données, dans un fichier PNG. Sert aux passes de recherche de glitchs de texture et d'éclairage.
public static class LookAround
{
    // Le joueur (et donc la caméra, les lampes allumées autour d'elle, les reflets) est amené près du point à regarder.
    public static string Go(Vector3 at)
    {
        var p = Object.FindObjectOfType<PlayerController>();
        if (p == null) return "pas de joueur";
        p.Teleport(at + Vector3.up * 0.95f, 0f);
        p.SetFrozen(true);
        return "ok";
    }

    // Rend la caméra du jeu depuis 'eye' vers 'target'.
    public static string Shot(Vector3 eye, Vector3 target, string file, int w = 960, int h = 540, float fov = 70f)
    {
        Camera cam = Camera.main;
        if (cam == null) return "pas de caméra";
        Vector3 pos = cam.transform.position;
        Quaternion rot = cam.transform.rotation;
        float oldFov = cam.fieldOfView;
        var rt = new RenderTexture(w, h, 24);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        cam.fieldOfView = fov;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        cam.transform.SetPositionAndRotation(pos, rot);
        cam.fieldOfView = oldFov;
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        return file;
    }
}
