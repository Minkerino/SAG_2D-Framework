using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImprovedHierarchy
{
    [Serializable]
    public class FolderColorEntry
    {
        public string folderGuid;
        public Color color = Color.white;
        public string iconName = "";
    }

    // Per-GameObject icon/colour customisation stored EXTERNALLY in the library
    // (keyed by GlobalObjectId) so we never have to add a MonoBehaviour to user
    // scene objects. This is what lets the package be uninstalled cleanly: when
    // the package is removed there is no orphan "Missing (Mono Script)" component
    // left behind on every customised GameObject.
    [Serializable]
    public class GameObjectIconEntry
    {
        public string globalObjectId;
        public Color iconColor = Color.white;
        public string internalIconName = "";
        public Texture2D customIconTexture;
    }

    public class IconLibrary : ScriptableObject
    {
        public List<string> favoriteInternalIcons = new List<string>();
        public List<Texture2D> customTextures = new List<Texture2D>();
        public List<FolderColorEntry> folderColors = new List<FolderColorEntry>();
        public List<string> favoriteScenes = new List<string>(); // scene GUIDs

        // NEW in 0.2.8: per-GameObject customisation moved off of MonoBehaviours
        // into the library. Lookup is via GlobalObjectId so the link survives
        // scene saves, prefab application, renames, and hierarchy reorders.
        public List<GameObjectIconEntry> gameObjectIcons = new List<GameObjectIconEntry>();
    }
}
