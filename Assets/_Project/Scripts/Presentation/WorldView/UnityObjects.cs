namespace WorldBeyond.Presentation.WorldView
{
    // Zerstört Laufzeit-Objekte im Spiel und im Editor gleichermaßen.
    static class UnityObjects
    {
        public static void Destroy(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
            if (UnityEngine.Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
