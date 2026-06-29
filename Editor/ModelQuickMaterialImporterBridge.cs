using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace ModelQuickMaterial
{
	/// <summary>
	/// Inspector 集成 + 核心材质重映射逻辑
	/// 通过 [InitializeOnLoad] 自动在 ModelImporter 的 Inspector 顶部注入按钮
	/// </summary>
	[InitializeOnLoad]
	public static class ModelQuickMaterialImporterBridge
	{
		// ============================================================
		// 静态构造 — 注册 Inspector 回调
		// ============================================================
		static ModelQuickMaterialImporterBridge()
		{
			Editor.finishedDefaultHeaderGUI += OnInspectorHeaderGUI;
		}

		// ============================================================
		// Inspector 头部 GUI 回调
		// ============================================================
		private static void OnInspectorHeaderGUI(Editor editor)
		{
			// 仅在 ModelImporter 的 Inspector 中显示按钮
			if (editor.target == null) return;
			if (!(editor.target is ModelImporter)) return;

			GUILayout.Space(4);

			var style = new GUIStyle(GUI.skin.button)
			{
				fontStyle = FontStyle.Bold,
				fixedHeight = 24
			};

			GUI.backgroundColor = new Color(0.4f, 0.8f, 0.5f);
			if (GUILayout.Button("快速材质匹配", style, GUILayout.Width(140)))
			{
				ModelQuickMaterialWindow.ShowWindow();
			}
			GUI.backgroundColor = Color.white;

			GUILayout.Space(4);
		}

		// ============================================================
		// 核心逻辑 — 获取 FBX 模型的材质槽位信息
		// ============================================================

		/// <summary>
		/// 通过 AssetDatabase.LoadAllAssetsAtPath 获取 FBX 中嵌入的材质列表
		/// 这些嵌入材质代表模型在 DCC 工具中的材质槽位
		/// </summary>
		/// <param name="fbxPath">FBX 文件的资源路径 (如 Assets/Models/xxx.fbx)</param>
		/// <returns>材质槽位信息列表</returns>
		public static List<MaterialSlotInfo> GetMaterialSlots(string fbxPath)
		{
			var slots = new List<MaterialSlotInfo>();

			if (string.IsNullOrEmpty(fbxPath)) return slots;

			// 加载 FBX 中的所有子资源
			var allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
			if (allAssets == null || allAssets.Length == 0) return slots;

			int index = 0;
			foreach (var asset in allAssets)
			{
				// 只收集 Material 类型的子资源
				if (asset is Material mat)
				{
					slots.Add(new MaterialSlotInfo
					{
						slotIndex = index,
						materialName = mat.name
					});
					index++;
				}
			}

			return slots;
		}

		/// <summary>
		/// 获取 FBX 模型中已存在的材质重映射
		/// </summary>
		/// <param name="fbxPath">FBX 资源路径</param>
		/// <returns>当前已重映射的材质字典 (sourceIdentifier → 外部材质)</returns>
		public static Dictionary<AssetImporter.SourceAssetIdentifier, Object> GetExistingRemaps(string fbxPath)
		{
			var importer = AssetImporter.GetAtPath(fbxPath);
			if (importer == null) return new Dictionary<AssetImporter.SourceAssetIdentifier, Object>();

			var map = importer.GetExternalObjectMap();
			return new Dictionary<AssetImporter.SourceAssetIdentifier, Object>(map);
		}

		// ============================================================
		// 核心逻辑 — 应用材质重映射
		// ============================================================

		/// <summary>
		/// 将匹配结果应用到 FBX 模型的材质重映射
		/// </summary>
		/// <param name="fbxPath">FBX 资源路径</param>
		/// <param name="matches">匹配结果列表</param>
		/// <returns>成功应用的材质数量</returns>
		public static int ApplyMaterialRemaps(string fbxPath, List<MaterialMatchResult> matches)
		{
			if (string.IsNullOrEmpty(fbxPath))
			{
				Debug.LogError("[快速材质匹配] FBX 路径为空");
				return 0;
			}

			if (matches == null || matches.Count == 0)
			{
				Debug.LogWarning("[快速材质匹配] 没有可应用的匹配结果");
				return 0;
			}

			var importer = AssetImporter.GetAtPath(fbxPath);
			if (importer == null)
			{
				Debug.LogError($"[快速材质匹配] 无法获取导入器: {fbxPath}");
				return 0;
			}

			// 1. 清除旧的材质重映射
			var existingMap = importer.GetExternalObjectMap();
			foreach (var kvp in existingMap)
			{
				importer.RemoveRemap(kvp.Key);
			}

			// 2. 添加新的重映射
			int appliedCount = 0;
			foreach (var match in matches)
			{
				if (match.matchedMaterial == null) continue;

				var sourceId = new AssetImporter.SourceAssetIdentifier(typeof(Material), match.sourceName);
				importer.AddRemap(sourceId, match.matchedMaterial);
				appliedCount++;
			}

			// 3. 保存并重新导入
			importer.SaveAndReimport();

			AssetDatabase.Refresh();
			Debug.Log($"[快速材质匹配] 已为 {fbxPath} 应用 {appliedCount} 个材质匹配");

			return appliedCount;
		}

		/// <summary>
		/// 清除 FBX 模型的所有材质重映射（恢复为嵌入材质）
		/// </summary>
		/// <param name="fbxPath">FBX 资源路径</param>
		public static void ClearMaterialRemaps(string fbxPath)
		{
			if (string.IsNullOrEmpty(fbxPath)) return;

			var importer = AssetImporter.GetAtPath(fbxPath);
			if (importer == null)
			{
				Debug.LogError($"[快速材质匹配] 无法获取导入器: {fbxPath}");
				return;
			}

			var existingMap = importer.GetExternalObjectMap();
			int count = existingMap.Count;

			foreach (var kvp in existingMap)
			{
				importer.RemoveRemap(kvp.Key);
			}

			importer.SaveAndReimport();
			AssetDatabase.Refresh();

			Debug.Log($"[快速材质匹配] 已清除 {fbxPath} 的 {count} 个材质重映射");
		}

		// ============================================================
		// 验证工具
		// ============================================================

		/// <summary>
		/// 检查指定的资源路径是否为有效的可导入模型文件
		/// </summary>
		public static bool IsValidModelPath(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath)) return false;

			var importer = AssetImporter.GetAtPath(assetPath);
			return importer is ModelImporter;
		}

		/// <summary>
		/// 检查路径是否指向 FBX 文件
		/// </summary>
		public static bool IsFbxPath(string assetPath)
		{
			return !string.IsNullOrEmpty(assetPath) &&
				   assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// 检查路径是否指向可导入的模型文件 (FBX, OBJ, DAE, blend 等)
		/// </summary>
		public static bool IsModelFile(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath)) return false;

			string ext = System.IO.Path.GetExtension(assetPath).ToLowerInvariant();
			return ext == ".fbx" || ext == ".obj" || ext == ".dae" || ext == ".blend" ||
				   ext == ".3ds" || ext == ".dxf" || ext == ".max" || ext == ".ma" ||
				   ext == ".mb" || ext == ".gltf" || ext == ".glb";
		}
	}

	// ============================================================
	// 数据结构定义
	// ============================================================

	/// <summary>
	/// FBX 模型中的材质槽位信息
	/// </summary>
	[System.Serializable]
	public struct MaterialSlotInfo
	{
		/// <summary>槽位索引 (0-based)</summary>
		public int slotIndex;

		/// <summary>材质名称（来自 FBX 嵌入材质）</summary>
		public string materialName;
	}

	/// <summary>
	/// 材质匹配结果
	/// </summary>
	[System.Serializable]
	public class MaterialMatchResult
	{
		/// <summary>对应的槽位索引</summary>
		public int slotIndex;

		/// <summary>源材质名称 (FBX 中嵌入材质的名称)</summary>
		public string sourceName;

		/// <summary>匹配到的项目材质 (null 表示未找到)</summary>
		public Material matchedMaterial;

		/// <summary>匹配到的材质资源路径</summary>
		public string matchedPath;

		/// <summary>是否为精确名称匹配</summary>
		public bool isExactMatch;

		/// <summary>用户是否手动覆盖了自动匹配</summary>
		public bool userOverride;
	}
}
