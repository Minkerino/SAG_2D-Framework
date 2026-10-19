using UnityEngine;

namespace ImprovedHierarchy
{
    // ---------------------------------------------------------------------------
    //  LEGACY component (package <= 0.2.7) kept around purely so existing scenes
    //  load without "Missing (Mono Script)" warnings during the upgrade window.
    //
    //  Nothing in the package adds this component any more — per-GameObject icon
    //  data now lives in the IconLibrary ScriptableObject (see GameObjectIconEntry
    //  / GameObjectIconStore). The editor lazily migrates any IconData it finds
    //  on a hierarchy redraw and queues the component for removal; users can also
    //  run Tools > Improved Hierarchy > Migrate Legacy IconData -> Library for a
    //  one-shot project-wide pass before uninstalling.
    //
    //  The class stays in the runtime assembly so the migration code in the
    //  editor can resolve the type via GetComponent<IconData>().
    // ---------------------------------------------------------------------------
    [AddComponentMenu("")]
    public class IconData : MonoBehaviour
    {
        [HideInInspector] public string internalIconName = "";
        [HideInInspector] public Texture2D customIconTexture;
        [HideInInspector] public Color iconColor = Color.white;

        private void OnValidate() => hideFlags = HideFlags.HideInInspector;
        private void Reset() => hideFlags = HideFlags.HideInInspector;
    }
}
