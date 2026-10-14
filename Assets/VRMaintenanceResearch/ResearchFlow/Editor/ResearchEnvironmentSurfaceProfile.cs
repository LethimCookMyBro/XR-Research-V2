#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Configure an owned transient copy only. The caller owns production asset guards and retention.
    // This material policy does not change a scene, lighting, render pipeline or study content.
    public static class ResearchEnvironmentSurfaceProfile
    {
        public const string FloorDetailPath = "Assets/VRMaintenanceResearch/Assets/V2/Environment/textures/PBR/EnvironmentClarity/ConcreteFloor03_Detail_2K.jpg";
        public const string FloorNormalPath = "Assets/VRMaintenanceResearch/Assets/V2/Environment/textures/PBR/EnvironmentClarity/ConcreteFloor03_Normal_1K.png";

        public static void Configure(Material ownedCopy, string role)
        {
            if (role != "UXS_Floor" && role != "UXS_Wall" && role != "SP_Paint" && role != "SP_Timber")
                throw new ArgumentException("Unsupported environment surface role: " + role, nameof(role));
            if (!ownedCopy) throw new ArgumentNullException(nameof(ownedCopy));
            if (EditorUtility.IsPersistent(ownedCopy))
                throw new InvalidOperationException("Configure an owned transient material copy, not a production asset.");
            if (!ownedCopy.shader || ownedCopy.shader.name != "Universal Render Pipeline/Lit")
                throw new InvalidOperationException("The environment surface profile requires installed URP Lit.");

            string[] properties = role == "UXS_Floor"
                ? new[] { "_BaseColor", "_BaseMap", "_DetailAlbedoMap", "_DetailAlbedoMapScale", "_DetailMask",
                    "_DetailNormalMap", "_DetailNormalMapScale", "_BumpMap", "_BumpScale", "_Smoothness",
                    "_Metallic", "_MetallicGlossMap", "_OcclusionMap" }
                : role == "SP_Paint" ? new[] { "_BaseColor", "_BaseMap", "_BumpScale", "_Smoothness" }
                : role == "UXS_Wall" ? new[] { "_BaseColor", "_BaseMap", "_BumpScale" }
                : new[] { "_BumpScale" };
            foreach (string property in properties)
                if (!ownedCopy.HasProperty(property))
                    throw new InvalidOperationException("Required URP Lit property is missing: " + property);

            // Validate every external dependency before the first material mutation.
            Texture2D detail = null, normal = null;
            if (role == "UXS_Floor")
            {
                detail = LoadFloorTexture(FloorDetailPath, false);
                normal = LoadFloorTexture(FloorNormalPath, true);
            }

            switch (role)
            {
                case "UXS_Floor":
                    ownedCopy.SetTexture("_BaseMap", null);
                    ownedCopy.SetTextureScale("_BaseMap", Vector2.one / 2.5f);
                    ownedCopy.SetTextureOffset("_BaseMap", Vector2.zero);
                    ownedCopy.SetTexture("_DetailAlbedoMap", detail);
                    ownedCopy.SetTextureScale("_DetailAlbedoMap", Vector2.one);
                    ownedCopy.SetTextureOffset("_DetailAlbedoMap", Vector2.zero);
                    ownedCopy.SetFloat("_DetailAlbedoMapScale", .12f);
                    ownedCopy.SetTexture("_DetailMask", null);
                    ownedCopy.SetTexture("_DetailNormalMap", null);
                    ownedCopy.SetFloat("_DetailNormalMapScale", 0);
                    ownedCopy.EnableKeyword("_DETAIL_SCALED");
                    ownedCopy.DisableKeyword("_DETAIL_MULX2");
                    ownedCopy.SetTexture("_BumpMap", normal);
                    ownedCopy.EnableKeyword("_NORMALMAP");
                    Tint(ownedCopy, .59f, .61f, .61f);
                    ownedCopy.SetFloat("_BumpScale", .18f);
                    ownedCopy.SetFloat("_Smoothness", .28f);
                    ownedCopy.SetFloat("_Metallic", 0);
                    ownedCopy.SetTexture("_MetallicGlossMap", null);
                    ownedCopy.SetTexture("_OcclusionMap", null);
                    ownedCopy.DisableKeyword("_METALLICSPECGLOSSMAP");
                    ownedCopy.DisableKeyword("_OCCLUSIONMAP");
                    break;
                case "UXS_Wall":
                    ownedCopy.SetTexture("_BaseMap", null);
                    Tint(ownedCopy, .92f, .92f, .90f);
                    ownedCopy.SetFloat("_BumpScale", .15f);
                    break;
                case "SP_Paint":
                    ownedCopy.SetTexture("_BaseMap", null);
                    Tint(ownedCopy, .18f, .28f, .30f);
                    ownedCopy.SetFloat("_BumpScale", .12f);
                    ownedCopy.SetFloat("_Smoothness", .35f);
                    break;
                case "SP_Timber":
                    ownedCopy.SetFloat("_BumpScale", .28f);
                    break;
            }
        }

        static void Tint(Material material, float r, float g, float b) =>
            material.SetColor("_BaseColor", new Color(r, g, b, material.GetColor("_BaseColor").a));

        static Texture2D LoadFloorTexture(string path, bool normal)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!texture || importer == null || importer.sRGBTexture || !importer.mipmapEnabled
                || importer.filterMode != FilterMode.Trilinear || importer.wrapMode != TextureWrapMode.Repeat
                || importer.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default))
                throw new InvalidOperationException("Floor texture requires linear, mipmapped, repeating trilinear import: " + path);
            return texture;
        }
    }
}
#endif
