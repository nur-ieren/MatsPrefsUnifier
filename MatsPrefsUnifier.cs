// MatsPrefsUnifier.cs
// 配置場所: Assets/nur_ieren/ 配下(Editorフォルダ不要。全体を #if UNITY_EDITOR で囲んでいます)
// 対応バージョン: Unity 2022.3.22f1
//
// 指定フォルダ内の lilToon マテリアルを検索し、
// テンプレートマテリアルの「影」「リムシェード」「距離フェード」設定を一括コピーするツールです。
//
// 対象シェーダー: lilToon に加えて、lilToon をベースにした派生シェーダー（Uzumore Shader など）も
//                 同様に検知して処理します（シェーダー名 または lilToon 共通プロパティの有無で判定）。
//
// 対象の指定方法は以下の2種類から選べます:
//   ・フォルダ指定    : 指定フォルダ（サブフォルダ含む）内の lilToon マテリアルすべてが対象
//   ・GameObject指定  : 指定GameObjectと配下の MeshRenderer / SkinnedMeshRenderer が
//                       参照している lilToon マテリアルが対象
//
// 使い方:
//   Unityメニュー > nur_ieren > MatsPrefsUnifier
//
// [高速化版]
//  - シェーダーのプロパティ一覧・値をテンプレートから "1回だけ" 読み取ってキャッシュ
//  - プロパティアクセスを文字列名ではなく int (PropertyToID) で行う
//  - AssetDatabase.StartAssetEditing/StopAssetEditing で一括インポートに変更
//  - 不要な AssetDatabase.Refresh() を削除
//  - ログはリスト+LabelFieldの逐次描画をやめ、1つのTextAreaにまとめて描画コストを削減
//  - 進捗バー（キャンセル可、更新間引きあり）を追加
//
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace nur_ieren
{
    public class MatsPrefsUnifier : EditorWindow
    {
        private enum TargetMode
        {
            Folder,
            GameObjectHierarchy
        }

        private TargetMode targetMode = TargetMode.GameObjectHierarchy;

        private DefaultAsset targetFolder;
        private bool includeSubfolders = true;

        private GameObject targetGameObject;
        private bool includeInactiveRenderers = true;

        private Material templateMaterial;
        private bool copyShadow = true;
        private bool copyRim = true;
        private bool copyDistanceFade = true;
        private bool copyKeywords = true;
        private bool verboseLog = true; // false にすると大量処理時にログ生成コストを削減できる

        private Vector2 scroll;
        private readonly StringBuilder logBuilder = new StringBuilder();

        private static readonly string[] ShadowPrefixes = { "_UseShadow", "_Shadow" };
        private static readonly string[] RimPrefixes = { "_UseRim", "_Rim" };
        private static readonly string[] DistanceFadePrefixes = { "_UseDistanceFade", "_DistanceFade" };

        // テンプレートから1回だけ読み取ってキャッシュするプロパティ情報
        private struct CachedProp
        {
            public int id;
            public string name; // HasProperty(int)が使えないUnityバージョン用のフォールバックにも利用
            public ShaderUtil.ShaderPropertyType type;
            public Color colorVal;
            public Vector4 vectorVal;
            public float floatVal;
            public Texture texVal;
            public Vector2 texOffset;
            public Vector2 texScale;
        }

        [MenuItem("nur_ieren/MatsPrefsUnifier")]
        private static void ShowWindow()
        {
            var window = GetWindow<MatsPrefsUnifier>("MatsPrefsUnifier");
            window.minSize = new Vector2(440, 420);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("対象の指定方法", EditorStyles.boldLabel);
            targetMode = (TargetMode)GUILayout.Toolbar(
                (int)targetMode,
                new[] { "フォルダ指定", "GameObject指定" });

            EditorGUILayout.Space();

            if (targetMode == TargetMode.Folder)
            {
                targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    new GUIContent("対象フォルダ", "この中に含まれる lilToon マテリアルが対象になります"),
                    targetFolder, typeof(DefaultAsset), false);

                includeSubfolders = EditorGUILayout.Toggle("サブフォルダも含める", includeSubfolders);
            }
            else
            {
                targetGameObject = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("対象GameObject", "このオブジェクトと配下の MeshRenderer / SkinnedMeshRenderer が持つ lilToon マテリアルが対象になります"),
                    targetGameObject, typeof(GameObject), true);

                includeInactiveRenderers = EditorGUILayout.Toggle(
                    new GUIContent("非アクティブなオブジェクトも含める", "OFFにすると非アクティブな子オブジェクトのレンダラーは検索対象から除外します"),
                    includeInactiveRenderers);
            }

            EditorGUILayout.Space();

            templateMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent("テンプレートマテリアル", "ここに設定されている値を他のマテリアルへコピーします"),
                templateMaterial, typeof(Material), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("コピーする項目", EditorStyles.boldLabel);
            copyShadow = EditorGUILayout.Toggle("影 (Shadow) 設定", copyShadow);
            copyRim = EditorGUILayout.Toggle("リムシェード (Rim) 設定", copyRim);
            copyDistanceFade = EditorGUILayout.Toggle("距離フェード (Distance Fade) 設定", copyDistanceFade);
            copyKeywords = EditorGUILayout.Toggle("関連するON/OFFキーワードも同期する", copyKeywords);
            verboseLog = EditorGUILayout.Toggle(
                new GUIContent("詳細ログを出力する", "大量マテリアル処理時はOFFにすると高速化します"),
                verboseLog);

            EditorGUILayout.Space();

            bool hasTarget = targetMode == TargetMode.Folder
                ? targetFolder != null
                : targetGameObject != null;
            bool canRun = hasTarget && templateMaterial != null && (copyShadow || copyRim || copyDistanceFade);

            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (GUILayout.Button("プレビュー（対象マテリアルを確認）", GUILayout.Height(28)))
                {
                    Preview();
                }

                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button("一括上書きを実行", GUILayout.Height(32)))
                {
                    if (EditorUtility.DisplayDialog(
                        "確認",
                        "対象の lilToon マテリアルの影・リム・距離フェード設定を上書きします。\n" +
                        "Ctrl+Z (Undo) で戻せますが、念のためプロジェクトのバックアップを推奨します。\n\n実行しますか？",
                        "実行する", "キャンセル"))
                    {
                        Execute();
                    }
                }
                GUI.backgroundColor = prevColor;
            }

            if (!canRun)
            {
                EditorGUILayout.HelpBox("対象（フォルダ または GameObject）・テンプレートマテリアルを指定し、コピー項目を1つ以上選んでください。", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ログ", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(180));
            // 1行ずつLabelFieldを積むとログが数百〜数千行になった際にOnGUIの描画コストが跳ね上がるため、
            // 1つのTextAreaにまとめて描画コストを一定に保つ
            EditorGUILayout.TextArea(logBuilder.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// lilToon 本体に加えて、lilToon のカスタムシェーダー機構で作られた派生シェーダー
        /// (Uzumore Shader など) も同じ扱いにするための判定。
        /// </summary>
        private static bool IsLilToonFamilyMaterial(Material mat)
        {
            if (mat == null || mat.shader == null) return false;

            string shaderName = mat.shader.name;

            // 1. シェーダー名で判定
            if (shaderName.Contains("lilToon")) return true;
            if (shaderName.IndexOf("Uzumore", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // 2. シェーダー名に依らない判定。
            //    lilToon 系は共通のプロパティ定義 (lilProperties "Default") を持つため、
            //    lilToon のバージョン情報プロパティの有無で派生シェーダーも拾える。
            return mat.HasProperty("_lilToonVersion");
        }

        private List<Material> FindTargetMaterials()
        {
            return targetMode == TargetMode.Folder
                ? FindTargetMaterialsFromFolder()
                : FindTargetMaterialsFromGameObject();
        }

        private List<Material> FindTargetMaterialsFromFolder()
        {
            var result = new List<Material>();
            if (targetFolder == null) return result;

            string folderPath = AssetDatabase.GetAssetPath(targetFolder);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return result;

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folderPath });

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!includeSubfolders)
                {
                    string dir = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
                    if (dir != folderPath) continue;
                }

                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null && IsLilToonFamilyMaterial(mat))
                {
                    result.Add(mat);
                }
            }
            return result;
        }

        /// <summary>
        /// 指定GameObjectと配下の MeshRenderer / SkinnedMeshRenderer が参照している
        /// lilToon マテリアルを重複無く収集する。
        /// </summary>
        private List<Material> FindTargetMaterialsFromGameObject()
        {
            var result = new List<Material>();
            if (targetGameObject == null) return result;

            var seen = new HashSet<Material>();
            var renderers = targetGameObject.GetComponentsInChildren<Renderer>(includeInactiveRenderers);

            foreach (var renderer in renderers)
            {
                // MeshRenderer/SkinnedMeshRenderer以外（ParticleSystemRendererなど）は対象外
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;

                var sharedMats = renderer.sharedMaterials;
                foreach (var mat in sharedMats)
                {
                    if (mat == null) continue;
                    if (!seen.Add(mat)) continue; // 同じマテリアルを複数レンダラーが共有していても1回だけ処理する

                    if (!IsLilToonFamilyMaterial(mat)) continue;

                    if (!EditorUtility.IsPersistent(mat))
                    {
                        // アセット化されていない（保存できない）マテリアルインスタンスは対象から除外
                        Debug.LogWarning($"[lilToon一括設定] '{renderer.gameObject.name}' の '{mat.name}' は" +
                            " アセットとして保存されていないマテリアルのため対象から除外しました。");
                        continue;
                    }

                    result.Add(mat);
                }
            }
            return result;
        }

        private void Preview()
        {
            logBuilder.Clear();
            var mats = FindTargetMaterials();
            logBuilder.AppendLine($"lilToon 系（Uzumore 含む）マテリアルが {mats.Count} 件見つかりました。");
            foreach (var m in mats)
            {
                logBuilder.AppendLine(" - " + AssetDatabase.GetAssetPath(m));
            }
            if (mats.Count == 0)
            {
                if (targetMode == TargetMode.Folder)
                {
                    logBuilder.AppendLine("対象フォルダ、または各マテリアルの Shader 設定を確認してください（lilToon 系である必要があります）。");
                }
                else
                {
                    logBuilder.AppendLine("対象GameObject配下に MeshRenderer / SkinnedMeshRenderer が無いか、lilToon マテリアルが割り当てられていない可能性があります。");
                }
            }
        }

        private void Execute()
        {
            logBuilder.Clear();

            if (!IsLilToonFamilyMaterial(templateMaterial))
            {
                logBuilder.AppendLine("エラー：テンプレートマテリアルの Shader が lilToon 系（lilToon / Uzumore Shader など）ではありません。");
                return;
            }

            var mats = FindTargetMaterials();
            if (mats.Count == 0)
            {
                logBuilder.AppendLine("対象マテリアルが見つかりませんでした。");
                return;
            }

            List<CachedProp> cachedProps = BuildCachedProps(templateMaterial, copyShadow, copyRim, copyDistanceFade);

            HashSet<string> relevantSrcKeywords = null;
            if (copyKeywords)
            {
                relevantSrcKeywords = new HashSet<string>();
                foreach (var kw in templateMaterial.shaderKeywords)
                {
                    if (IsRelevantKeyword(kw)) relevantSrcKeywords.Add(kw);
                }
            }

            int successCount = 0;
            int skipCount = 0;
            int total = mats.Count;
            bool canceled = false;

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < total; i++)
                {
                    // 進捗バーの更新自体にもコストがあるため、間引いて呼び出す
                    if (i % 10 == 0 || i == total - 1)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                            "lilToon 一括処理中...",
                            $"{i + 1} / {total} 件処理中",
                            (float)i / total))
                        {
                            canceled = true;
                            break;
                        }
                    }

                    var mat = mats[i];
                    if (mat == templateMaterial) { skipCount++; continue; }

                    Undo.RecordObject(mat, "lilToon Shadow/Rim Batch Copy");

                    int copiedProps = ApplyCachedProps(mat, cachedProps);

                    if (copyKeywords)
                    {
                        CopyKeywords(mat, relevantSrcKeywords);
                    }

                    EditorUtility.SetDirty(mat);
                    successCount++;

                    if (verboseLog)
                    {
                        logBuilder.AppendLine($"OK ({copiedProps}項目更新): {AssetDatabase.GetAssetPath(mat)}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            // 新規/削除アセットは無いので Refresh() は不要。SaveAssets のみで十分。
            AssetDatabase.SaveAssets();

            logBuilder.AppendLine("----------------------------");
            if (canceled)
            {
                logBuilder.AppendLine($"キャンセルされました。途中まで {successCount} 件を更新済みです。");
            }
            else
            {
                logBuilder.AppendLine($"完了：{successCount} 件のマテリアルを更新しました。（テンプレート自身をスキップ：{skipCount}件）");
            }
        }

        /// <summary>
        /// テンプレートマテリアルから、対象プレフィックスに一致するプロパティの
        /// 値を「1回だけ」読み取ってキャッシュする。
        /// </summary>
        private static List<CachedProp> BuildCachedProps(Material src, bool includeShadow, bool includeRim, bool includeDistanceFade)
        {
            var list = new List<CachedProp>();
            Shader shader = src.shader;
            int propCount = ShaderUtil.GetPropertyCount(shader);

            for (int i = 0; i < propCount; i++)
            {
                string propName = ShaderUtil.GetPropertyName(shader, i);

                bool match =
                    (includeShadow && StartsWithAny(propName, ShadowPrefixes)) ||
                    (includeRim && StartsWithAny(propName, RimPrefixes)) ||
                    (includeDistanceFade && StartsWithAny(propName, DistanceFadePrefixes));
                if (!match) continue;

                var type = ShaderUtil.GetPropertyType(shader, i);
                var cached = new CachedProp
                {
                    id = Shader.PropertyToID(propName),
                    name = propName,
                    type = type
                };

                switch (type)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        cached.colorVal = src.GetColor(cached.id);
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        cached.vectorVal = src.GetVector(cached.id);
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        cached.floatVal = src.GetFloat(cached.id);
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        cached.texVal = src.GetTexture(cached.id);
                        cached.texOffset = src.GetTextureOffset(cached.id);
                        cached.texScale = src.GetTextureScale(cached.id);
                        break;
                    default:
                        continue; // 未対応の型はスキップ
                }

                list.Add(cached);
            }
            return list;
        }

        private static bool StartsWithAny(string value, string[] prefixes)
        {
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (value.StartsWith(prefixes[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// キャッシュ済みプロパティを対象マテリアルへ適用する。
        /// 文字列名ではなく int (PropertyID) でアクセスするため、
        /// マテリアル1件あたりの処理コストを抑えられる。
        /// </summary>
        private static int ApplyCachedProps(Material dst, List<CachedProp> cachedProps)
        {
            int copied = 0;
            for (int i = 0; i < cachedProps.Count; i++)
            {
                var p = cachedProps[i];
                if (!dst.HasProperty(p.id)) continue;

                switch (p.type)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        dst.SetColor(p.id, p.colorVal);
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        dst.SetVector(p.id, p.vectorVal);
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        dst.SetFloat(p.id, p.floatVal);
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        dst.SetTexture(p.id, p.texVal);
                        dst.SetTextureOffset(p.id, p.texOffset);
                        dst.SetTextureScale(p.id, p.texScale);
                        break;
                    default:
                        continue;
                }
                copied++;
            }
            return copied;
        }

        private static bool IsRelevantKeyword(string kw)
        {
            return kw.Contains("SHADOW") || kw.Contains("RIM") || kw.Contains("DISTANCEFADE") || kw.Contains("DISTANCE_FADE");
        }

        private static void CopyKeywords(Material dst, HashSet<string> relevantSrcKeywords)
        {
            var current = dst.shaderKeywords;
            var newKeywords = new List<string>(current.Length + relevantSrcKeywords.Count);

            // 既存キーワードのうち、SHADOW/RIM関連以外はそのまま残す
            for (int i = 0; i < current.Length; i++)
            {
                if (!IsRelevantKeyword(current[i]))
                {
                    newKeywords.Add(current[i]);
                }
            }
            newKeywords.AddRange(relevantSrcKeywords);

            dst.shaderKeywords = newKeywords.ToArray();
        }
    }
}
#endif
