using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace ModelQuickMaterial
{
	/// <summary>
	/// 模型快速材质匹配主窗口
	/// 提供项目文件夹树浏览、材质搜索匹配、批量应用等功能
	/// </summary>
	public class ModelQuickMaterialWindow : EditorWindow
	{
		// ============================================================
		// 静态入口
		// ============================================================

		/// <summary>右键菜单入口 — 在 FBX 资源上右键可见</summary>
		[MenuItem("Assets/快速材质匹配", false, 30)]
		public static void ShowWindow()
		{
			var window = GetWindow<ModelQuickMaterialWindow>("模型快速材质匹配");
			window.minSize = new Vector2(480, 600);
			window.Show();
			window.AutoDetectSelection();
		}

		/// <summary>右键菜单验证 — 仅在选择 FBX 或模型文件时可用</summary>
		[MenuItem("Assets/快速材质匹配", true)]
		public static bool ShowWindowValidate()
		{
			var selected = Selection.activeObject;
			if (selected == null) return false;
			var path = AssetDatabase.GetAssetPath(selected);
			return !string.IsNullOrEmpty(path) && ModelQuickMaterialImporterBridge.IsModelFile(path);
		}

		/// <summary>Window 菜单入口</summary>
		[MenuItem("Window/快速材质匹配")]
		public static void ShowWindowFromMenu()
		{
			ShowWindow();
		}

		// ============================================================
		// 私有字段
		// ============================================================

		// 目标模型
		private string m_targetPath = "";
		private List<MaterialSlotInfo> m_materialSlots = new List<MaterialSlotInfo>();

		// 文件夹树
		private List<string> m_folderTreeRoots = new List<string>();
		private Dictionary<string, bool> m_folderExpanded = new Dictionary<string, bool>();
		private string m_selectedFolderPath = "Assets";
		private string m_folderFilter = "";
		private Vector2 m_folderTreeScrollPos;
		private bool m_searchSubfolders = true;

		// 匹配
		private bool m_exactMatch = false;
		private List<MaterialMatchResult> m_matchResults = new List<MaterialMatchResult>();
		private bool m_hasSearched = false;

		// 滚动 & 折叠
		private Vector2 m_slotsScrollPos;
		private Vector2 m_previewScrollPos;
		private bool m_showSlotList = true;
		private bool m_showSearchSettings = true;
		private bool m_showPreview = true;

		// 样式缓存
		private GUIStyle m_folderItemStyle;
		private GUIStyle m_selectedFolderStyle;
		private GUIStyle m_matchOkStyle;
		private GUIStyle m_matchFailStyle;
		private bool m_stylesBuilt = false;

		// ============================================================
		// 生命周期
		// ============================================================

		private void OnEnable()
		{
			Selection.selectionChanged += OnSelectionChanged;

			// 恢复上次的搜索路径
			m_selectedFolderPath = EditorPrefs.GetString("MQM_SearchFolder", "Assets");
			m_searchSubfolders = EditorPrefs.GetBool("MQM_SearchSubfolders", true);
			m_exactMatch = EditorPrefs.GetBool("MQM_ExactMatch", false);
			m_showSlotList = EditorPrefs.GetBool("MQM_ShowSlots", true);
			m_showSearchSettings = EditorPrefs.GetBool("MQM_ShowSearch", true);
			m_showPreview = EditorPrefs.GetBool("MQM_ShowPreview", true);

			// 确保选中的文件夹有效
			if (!AssetDatabase.IsValidFolder(m_selectedFolderPath))
			{
				m_selectedFolderPath = "Assets";
			}

			BuildFolderTree();
			AutoDetectSelection();
		}

		private void OnDisable()
		{
			Selection.selectionChanged -= OnSelectionChanged;

			// 保存状态
			EditorPrefs.SetString("MQM_SearchFolder", m_selectedFolderPath);
			EditorPrefs.SetBool("MQM_SearchSubfolders", m_searchSubfolders);
			EditorPrefs.SetBool("MQM_ExactMatch", m_exactMatch);
			EditorPrefs.SetBool("MQM_ShowSlots", m_showSlotList);
			EditorPrefs.SetBool("MQM_ShowSearch", m_showSearchSettings);
			EditorPrefs.SetBool("MQM_ShowPreview", m_showPreview);
		}

		// ============================================================
		// 选中变化回调
		// ============================================================

		private void OnSelectionChanged()
		{
			AutoDetectSelection();
		}

		/// <summary>
		/// 自动检测当前选中的对象是否为 FBX 模型
		/// </summary>
		private void AutoDetectSelection()
		{
			var selected = Selection.activeObject;
			if (selected == null) return;

			var path = AssetDatabase.GetAssetPath(selected);
			if (!string.IsNullOrEmpty(path) && ModelQuickMaterialImporterBridge.IsModelFile(path))
			{
				if (m_targetPath != path)
				{
					m_targetPath = path;
					LoadMaterialSlots();
					m_matchResults.Clear();
					m_hasSearched = false;
					Repaint();
				}
			}
		}

		// ============================================================
		// 加载材质槽位
		// ============================================================

		private void LoadMaterialSlots()
		{
			m_materialSlots = ModelQuickMaterialImporterBridge.GetMaterialSlots(m_targetPath);

			if (m_materialSlots.Count == 0)
			{
				Debug.Log($"[快速材质匹配] 模型 {Path.GetFileName(m_targetPath)} 没有嵌入材质");
			}
		}

		// ============================================================
		// 文件夹树
		// ============================================================

		/// <summary>
		/// 递归构建项目文件夹树
		/// </summary>
		private void BuildFolderTree()
		{
			m_folderTreeRoots.Clear();
			CollectFolders("Assets", m_folderTreeRoots);
		}

		private void CollectFolders(string parentFolder, List<string> result)
		{
			result.Add(parentFolder);
			string[] subFolders = AssetDatabase.GetSubFolders(parentFolder);
			foreach (string sub in subFolders)
			{
				CollectFolders(sub, result);
			}
		}

		// ============================================================
		// 材质搜索匹配
		// ============================================================

		private void SearchMaterials()
		{
			if (m_materialSlots.Count == 0)
			{
				EditorUtility.DisplayDialog("提示", "当前模型没有材质槽位，无法搜索", "确定");
				return;
			}

			if (string.IsNullOrEmpty(m_selectedFolderPath) ||
				!AssetDatabase.IsValidFolder(m_selectedFolderPath))
			{
				EditorUtility.DisplayDialog("提示", "请先选择一个有效的搜索文件夹", "确定");
				return;
			}

			try
			{
				// 1. 构建搜索路径列表
				EditorUtility.DisplayProgressBar("快速材质匹配", "正在收集搜索路径...", 0.1f);

				List<string> searchPaths = new List<string>();
				searchPaths.Add(m_selectedFolderPath);

				if (m_searchSubfolders)
				{
					CollectFolders(m_selectedFolderPath, searchPaths);
					// CollectFolders 会把 m_selectedFolderPath 加入两次，去重
					searchPaths = searchPaths.Distinct().ToList();
				}

				// 2. 搜索所有材质
				EditorUtility.DisplayProgressBar("快速材质匹配", "正在搜索材质...", 0.2f);

				string[] guids = AssetDatabase.FindAssets("t:Material", searchPaths.ToArray());
				List<Material> allMaterials = new List<Material>();

				for (int i = 0; i < guids.Length; i++)
				{
					string matPath = AssetDatabase.GUIDToAssetPath(guids[i]);
					var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

					// 过滤掉 FBX 内的嵌入材质（避免自我匹配）
					if (mat != null && matPath != m_targetPath)
					{
						allMaterials.Add(mat);
					}

					if (i % 20 == 0)
					{
						EditorUtility.DisplayProgressBar("快速材质匹配",
							$"正在加载材质... ({i + 1}/{guids.Length})",
							0.3f + 0.4f * ((float)i / guids.Length));
					}
				}

				if (allMaterials.Count == 0)
				{
					// 清除进度条后再弹窗
					EditorUtility.ClearProgressBar();
					EditorUtility.DisplayDialog("提示",
						$"在文件夹 \"{m_selectedFolderPath}\" 中未找到任何材质",
						"确定");
					return;
				}

				// 3. 为每个槽位匹配材质
				EditorUtility.DisplayProgressBar("快速材质匹配", "正在匹配材质名称...", 0.8f);

				m_matchResults.Clear();

				for (int i = 0; i < m_materialSlots.Count; i++)
				{
					var slot = m_materialSlots[i];
					var bestMatch = FindBestMatch(slot.materialName, allMaterials);

					m_matchResults.Add(new MaterialMatchResult
					{
						slotIndex = slot.slotIndex,
						sourceName = slot.materialName,
						matchedMaterial = bestMatch.matchedMat,
						matchedPath = bestMatch.matchedPath,
						isExactMatch = bestMatch.isExact,
						userOverride = false
					});
				}

				m_hasSearched = true;

				int matchedCount = m_matchResults.Count(r => r.matchedMaterial != null);
				Debug.Log($"[快速材质匹配] 搜索完成: {matchedCount}/{m_matchResults.Count} 个槽位匹配成功");
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				Repaint();
			}
		}

		/// <summary>
		/// 为单个材质名称查找最佳匹配
		/// </summary>
		private (Material matchedMat, string matchedPath, bool isExact) FindBestMatch(
			string slotName, List<Material> candidates)
		{
			Material exactMatch = null;
			string exactPath = "";
			Material fuzzyMatch = null;
			string fuzzyPath = "";

			foreach (var mat in candidates)
			{
				string matName = mat.name;

				if (string.Equals(matName, slotName, System.StringComparison.OrdinalIgnoreCase))
				{
					// 精确匹配
					if (exactMatch == null)
					{
						exactMatch = mat;
						exactPath = AssetDatabase.GetAssetPath(mat);
					}
				}
				else if (!m_exactMatch && matName.IndexOf(slotName,
					System.StringComparison.OrdinalIgnoreCase) >= 0)
				{
					// 模糊匹配（包含即可）
					if (fuzzyMatch == null)
					{
						fuzzyMatch = mat;
						fuzzyPath = AssetDatabase.GetAssetPath(mat);
					}
				}
			}

			if (exactMatch != null)
				return (exactMatch, exactPath, true);

			if (fuzzyMatch != null)
				return (fuzzyMatch, fuzzyPath, false);

			return (null, "", false);
		}

		// ============================================================
		// 应用 / 清除
		// ============================================================

		private void ApplyMatches()
		{
			if (m_matchResults.Count == 0)
			{
				EditorUtility.DisplayDialog("提示", "没有可应用的匹配结果，请先搜索", "确定");
				return;
			}

			int matchCount = m_matchResults.Count(r => r.matchedMaterial != null);
			if (matchCount == 0)
			{
				EditorUtility.DisplayDialog("提示", "所有材质槽位都未匹配，无法应用", "确定");
				return;
			}

			if (!EditorUtility.DisplayDialog("确认应用",
				$"将为 {Path.GetFileName(m_targetPath)} 应用 {matchCount} 个材质匹配，是否继续？",
				"应用", "取消"))
			{
				return;
			}

			int applied = ModelQuickMaterialImporterBridge.ApplyMaterialRemaps(m_targetPath, m_matchResults);

			EditorUtility.DisplayDialog("完成",
				$"已成功应用 {applied} 个材质匹配到:\n{Path.GetFileName(m_targetPath)}",
				"确定");

			Repaint();
		}

		private void ClearMatches()
		{
			if (string.IsNullOrEmpty(m_targetPath))
			{
				EditorUtility.DisplayDialog("提示", "请先选择一个 FBX 模型", "确定");
				return;
			}

			if (!EditorUtility.DisplayDialog("确认清除",
				$"将清除 {Path.GetFileName(m_targetPath)} 的所有材质重映射，恢复为默认嵌入材质，是否继续？",
				"清除", "取消"))
			{
				return;
			}

			ModelQuickMaterialImporterBridge.ClearMaterialRemaps(m_targetPath);
			m_matchResults.Clear();
			m_hasSearched = false;

			EditorUtility.DisplayDialog("完成",
				$"已清除 {Path.GetFileName(m_targetPath)} 的材质重映射",
				"确定");

			Repaint();
		}

		// ============================================================
		// 主 GUI
		// ============================================================

		private void OnGUI()
		{
			BuildStyles();
			DrawHeader();
			EditorGUILayout.Space(6);

			DrawModelInfo();
			EditorGUILayout.Space(4);

			DrawMaterialSlotsSection();
			EditorGUILayout.Space(4);

			DrawSearchSection();
			EditorGUILayout.Space(4);

			if (m_hasSearched)
			{
				DrawPreviewSection();
				EditorGUILayout.Space(6);
				DrawActionButtons();
			}
		}

		// ============================================================
		// UI 子区域
		// ============================================================

		private void DrawHeader()
		{
			EditorGUILayout.LabelField("模型快速材质匹配", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("为 FBX 模型按名称批量匹配项目材质", EditorStyles.miniLabel);
		}

		private void DrawModelInfo()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);

			EditorGUILayout.LabelField("目标模型", EditorStyles.boldLabel);

			// 允许用户手动拖入或自动检测
			EditorGUI.BeginChangeCheck();
			var selectedObj = AssetDatabase.LoadAssetAtPath<Object>(m_targetPath);
			var newObj = EditorGUILayout.ObjectField("FBX 模型", selectedObj, typeof(Object), false);
			if (EditorGUI.EndChangeCheck() && newObj != null)
			{
				string newPath = AssetDatabase.GetAssetPath(newObj);
				if (ModelQuickMaterialImporterBridge.IsModelFile(newPath))
				{
					m_targetPath = newPath;
					LoadMaterialSlots();
					m_matchResults.Clear();
					m_hasSearched = false;
				}
			}

			if (!string.IsNullOrEmpty(m_targetPath))
			{
				EditorGUILayout.LabelField("路径:", m_targetPath, EditorStyles.miniLabel);

				int slotCount = m_materialSlots.Count;
				EditorGUILayout.LabelField($"材质槽位数: {slotCount}",
					slotCount > 0 ? EditorStyles.miniLabel : EditorStyles.miniLabel);
			}
			else
			{
				EditorGUILayout.HelpBox("请在 Project 窗口中选择一个 FBX 模型，或拖入上方的对象框",
					MessageType.Info);
			}

			EditorGUILayout.EndVertical();
		}

		private void DrawMaterialSlotsSection()
		{
			m_showSlotList = EditorGUILayout.Foldout(m_showSlotList,
				$"材质槽位列表 ({m_materialSlots.Count})", true);

			if (!m_showSlotList) return;

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);

			if (m_materialSlots.Count == 0)
			{
				if (string.IsNullOrEmpty(m_targetPath))
				{
					EditorGUILayout.LabelField("请先选择 FBX 模型", EditorStyles.centeredGreyMiniLabel);
				}
				else
				{
					EditorGUILayout.LabelField("该模型没有嵌入材质，无需匹配",
						EditorStyles.centeredGreyMiniLabel);
				}
			}
			else
			{
				m_slotsScrollPos = EditorGUILayout.BeginScrollView(m_slotsScrollPos,
					GUILayout.MaxHeight(120));

				for (int i = 0; i < m_materialSlots.Count; i++)
				{
					var slot = m_materialSlots[i];

					EditorGUILayout.BeginHorizontal();

					// 槽位编号
					EditorGUILayout.LabelField($"[{slot.slotIndex}]", GUILayout.Width(30));

					// 材质名称
					EditorGUILayout.LabelField(slot.materialName);

					// 搜索状态指示
					if (m_hasSearched)
					{
						var match = m_matchResults.FirstOrDefault(r => r.slotIndex == slot.slotIndex);
						if (match != null)
						{
							GUI.color = match.matchedMaterial != null ? Color.green : Color.red;
							EditorGUILayout.LabelField(match.matchedMaterial != null ? "✓" : "✗",
								GUILayout.Width(20));
							GUI.color = Color.white;
						}
					}

					EditorGUILayout.EndHorizontal();
				}

				EditorGUILayout.EndScrollView();
			}

			EditorGUILayout.EndVertical();
		}

		private void DrawSearchSection()
		{
			m_showSearchSettings = EditorGUILayout.Foldout(m_showSearchSettings, "搜索设置", true);

			if (!m_showSearchSettings) return;

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);

			// 文件夹过滤
			EditorGUILayout.LabelField("搜索文件夹:", EditorStyles.boldLabel);

			// 文件夹过滤输入
			EditorGUI.BeginChangeCheck();
			m_folderFilter = EditorGUILayout.TextField("过滤:", m_folderFilter);
			if (EditorGUI.EndChangeCheck())
			{
				// 过滤时自动展开匹配的父目录
				if (!string.IsNullOrEmpty(m_folderFilter))
				{
					ExpandFoldersMatchingFilter(m_folderFilter);
				}
				else
				{
					RebuildFolderTree();
				}
			}

			// 文件夹树
			m_folderTreeScrollPos = EditorGUILayout.BeginScrollView(m_folderTreeScrollPos,
				GUILayout.Height(160));

			List<string> displayFolders;
			if (string.IsNullOrEmpty(m_folderFilter))
			{
				displayFolders = m_folderTreeRoots;
			}
			else
			{
				displayFolders = m_folderTreeRoots
					.Where(f => f.IndexOf(m_folderFilter,
						System.StringComparison.OrdinalIgnoreCase) >= 0)
					.ToList();
			}

			foreach (string folderPath in displayFolders)
			{
				DrawFolderTreeItem(folderPath);
			}

			EditorGUILayout.EndScrollView();

			EditorGUILayout.Space(2);

			// 当前选中
			EditorGUILayout.LabelField($"已选择: {m_selectedFolderPath}", EditorStyles.miniLabel);

			EditorGUILayout.Space(4);

			// 选项
			EditorGUILayout.BeginHorizontal();
			m_searchSubfolders = EditorGUILayout.ToggleLeft("包含子文件夹", m_searchSubfolders,
				GUILayout.Width(120));
			m_exactMatch = EditorGUILayout.ToggleLeft("精确匹配", m_exactMatch, GUILayout.Width(100));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.LabelField(m_exactMatch
				? "名称完全一致才算匹配"
				: "材质名称包含槽位名称即算匹配（默认）",
				EditorStyles.miniLabel);

			EditorGUILayout.Space(4);

			// 搜索按钮
			GUI.backgroundColor = new Color(0.4f, 0.7f, 1.0f);
			bool canSearch = !string.IsNullOrEmpty(m_targetPath) && m_materialSlots.Count > 0;
			GUI.enabled = canSearch;
			if (GUILayout.Button("🔍  搜索匹配", GUILayout.Height(28)))
			{
				SearchMaterials();
			}
			GUI.enabled = true;
			GUI.backgroundColor = Color.white;

			if (!canSearch)
			{
				EditorGUILayout.LabelField("请先选择一个带材质的 FBX 模型", EditorStyles.miniLabel);
			}

			EditorGUILayout.EndVertical();
		}

		private void DrawPreviewSection()
		{
			int matchedCount = m_matchResults.Count(r => r.matchedMaterial != null);
			int totalCount = m_matchResults.Count;

			m_showPreview = EditorGUILayout.Foldout(m_showPreview,
				$"匹配预览   [{matchedCount}/{totalCount}]", true);

			if (!m_showPreview) return;

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);

			m_previewScrollPos = EditorGUILayout.BeginScrollView(m_previewScrollPos,
				GUILayout.MaxHeight(200));

			for (int i = 0; i < m_matchResults.Count; i++)
			{
				var match = m_matchResults[i];
				DrawMatchRow(match, i);
			}

			EditorGUILayout.EndScrollView();

			EditorGUILayout.EndVertical();
		}

		private void DrawMatchRow(MaterialMatchResult match, int index)
		{
			EditorGUILayout.BeginHorizontal();

			// 槽位编号
			EditorGUILayout.LabelField($"[{match.slotIndex}]", GUILayout.Width(28));

			// 源名称
			EditorGUILayout.LabelField(match.sourceName, GUILayout.Width(140));

			// 箭头
			EditorGUILayout.LabelField("→", GUILayout.Width(16));

			// 匹配结果
			if (match.matchedMaterial != null)
			{
				// 允许用户手动覆盖
				var newMat = (Material)EditorGUILayout.ObjectField(match.matchedMaterial,
					typeof(Material), false, GUILayout.Width(150));

				if (newMat != match.matchedMaterial)
				{
					match.matchedMaterial = newMat;
					match.matchedPath = newMat != null ? AssetDatabase.GetAssetPath(newMat) : "";
					match.userOverride = true;
				}

				// 路径提示
				EditorGUILayout.LabelField(
					match.matchedPath.Replace(m_selectedFolderPath + "/", ".../"),
					EditorStyles.miniLabel);

				// 状态图标
				GUI.color = Color.green;
				EditorGUILayout.LabelField(match.isExactMatch ? "✓ 精确" : "✓ 模糊",
					m_matchOkStyle ?? EditorStyles.miniLabel, GUILayout.Width(50));
				GUI.color = Color.white;
			}
			else
			{
				// 未匹配 — 用户可手动拖入材质
				var newMat = (Material)EditorGUILayout.ObjectField(null,
					typeof(Material), false, GUILayout.Width(150));

				if (newMat != null)
				{
					match.matchedMaterial = newMat;
					match.matchedPath = AssetDatabase.GetAssetPath(newMat);
					match.userOverride = true;
				}

				EditorGUILayout.LabelField("(未找到匹配)", EditorStyles.miniLabel);

				GUI.color = Color.red;
				EditorGUILayout.LabelField("✗", m_matchFailStyle ?? EditorStyles.miniLabel,
					GUILayout.Width(20));
				GUI.color = Color.white;
			}

			EditorGUILayout.EndHorizontal();
		}

		private void DrawActionButtons()
		{
			EditorGUILayout.BeginHorizontal();

			// 应用按钮
			int matchCount = m_matchResults.Count(r => r.matchedMaterial != null);
			GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
			GUI.enabled = matchCount > 0;
			if (GUILayout.Button($"应用匹配 ({matchCount})", GUILayout.Height(32)))
			{
				ApplyMatches();
			}
			GUI.enabled = true;
			GUI.backgroundColor = Color.white;

			// 清除按钮
			GUI.backgroundColor = new Color(1.0f, 0.7f, 0.5f);
			if (GUILayout.Button("清除匹配", GUILayout.Height(32)))
			{
				ClearMatches();
			}
			GUI.backgroundColor = Color.white;

			// 重新搜索按钮
			if (GUILayout.Button("重新搜索", GUILayout.Height(32)))
			{
				SearchMaterials();
			}

			EditorGUILayout.EndHorizontal();
		}

		// ============================================================
		// 文件夹树绘制
		// ============================================================

		private void DrawFolderTreeItem(string folderPath)
		{
			// 计算缩进层级
			int depth = folderPath.Count(c => c == '/') - ("Assets".Count(c => c == '/'));
			if (depth < 0) depth = 0;

			// 检查是否有子文件夹
			string[] subFolders = AssetDatabase.GetSubFolders(folderPath);
			bool hasChildren = subFolders.Length > 0;

			// 确保展开状态存在
			if (!m_folderExpanded.ContainsKey(folderPath))
				m_folderExpanded[folderPath] = false;

			EditorGUILayout.BeginHorizontal();

			// 缩进
			GUILayout.Space(depth * 16f);

			// 展开/折叠 按钮
			if (hasChildren)
			{
				string foldIcon = m_folderExpanded[folderPath] ? "▼" : "▶";
				if (GUILayout.Button(foldIcon, EditorStyles.label, GUILayout.Width(16)))
				{
					m_folderExpanded[folderPath] = !m_folderExpanded[folderPath];
				}
			}
			else
			{
				GUILayout.Space(16);
			}

			// 文件夹图标 + 名称
			string folderName = Path.GetFileName(folderPath);
			if (string.IsNullOrEmpty(folderName)) folderName = "Assets";

			bool isSelected = (m_selectedFolderPath == folderPath);

			GUIStyle style = isSelected ? m_selectedFolderStyle : m_folderItemStyle;

			if (GUILayout.Button($"📁 {folderName}", style, GUILayout.Height(20)))
			{
				m_selectedFolderPath = folderPath;
				EditorPrefs.SetString("MQM_SearchFolder", folderPath);
				GUI.FocusControl(null);
			}

			EditorGUILayout.EndHorizontal();

			// 递归绘制子文件夹
			if (hasChildren && m_folderExpanded[folderPath])
			{
				foreach (string sub in subFolders)
				{
					DrawFolderTreeItem(sub);
				}
			}
		}

		// ============================================================
		// 辅助方法
		// ============================================================

		private void BuildStyles()
		{
			if (m_stylesBuilt) return;
			m_stylesBuilt = true;

			m_folderItemStyle = new GUIStyle(EditorStyles.label)
			{
				normal = { textColor = Color.white },
				hover = { textColor = new Color(0.7f, 0.9f, 1f) },
				padding = new RectOffset(2, 4, 2, 2)
			};

			m_selectedFolderStyle = new GUIStyle(EditorStyles.label)
			{
				normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.25f, 0.5f, 0.8f, 0.6f)) },
				padding = new RectOffset(2, 4, 2, 2)
			};

			m_matchOkStyle = new GUIStyle(EditorStyles.miniLabel)
			{
				normal = { textColor = Color.green }
			};

			m_matchFailStyle = new GUIStyle(EditorStyles.miniLabel)
			{
				normal = { textColor = Color.red }
			};
		}

		/// <summary>
		/// 创建纯色纹理（用于高亮背景）
		/// </summary>
		private Texture2D MakeTex(int width, int height, Color col)
		{
			Color[] pix = new Color[width * height];
			for (int i = 0; i < pix.Length; i++)
				pix[i] = col;

			Texture2D result = new Texture2D(width, height);
			result.SetPixels(pix);
			result.Apply();
			return result;
		}

		/// <summary>
		/// 展开包含过滤关键词的文件夹
		/// </summary>
		private void ExpandFoldersMatchingFilter(string filter)
		{
			foreach (string folder in m_folderTreeRoots)
			{
				if (folder.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
				{
					// 展开该文件夹及其所有父级
					ExpandToFolder(folder);
				}
			}
		}

		private void ExpandToFolder(string folderPath)
		{
			m_folderExpanded[folderPath] = true;
			string parent = GetParentFolder(folderPath);
			while (!string.IsNullOrEmpty(parent))
			{
				m_folderExpanded[parent] = true;
				parent = GetParentFolder(parent);
			}
		}

		private string GetParentFolder(string folderPath)
		{
			if (string.IsNullOrEmpty(folderPath) || folderPath == "Assets")
				return null;

			int lastSlash = folderPath.LastIndexOf('/');
			if (lastSlash <= 0) return "Assets";

			return folderPath.Substring(0, lastSlash);
		}

		private void RebuildFolderTree()
		{
			m_folderTreeRoots.Clear();
			CollectFolders("Assets", m_folderTreeRoots);
		}
	}
}
