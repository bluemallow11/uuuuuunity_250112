using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class BatchPackageImporter : EditorWindow
{
    private List<string> packagePaths = new List<string>();
    private Vector2 scrollPosition;
    private bool importInteractive = false;
    
    // 순차 임포트를 위한 변수들
    private bool isImporting = false;
    private int currentImportIndex = 0;
    private int successCount = 0;
    private int failCount = 0;

    [MenuItem("Tools/Batch Package Importer")]
    public static void ShowWindow()
    {
        GetWindow<BatchPackageImporter>("Package Importer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Batch Unity Package Importer", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 임포트 중일 때는 UI 비활성화
        GUI.enabled = !isImporting;

        // 임포트 옵션
        importInteractive = EditorGUILayout.Toggle("Interactive Import", importInteractive);
        EditorGUILayout.HelpBox(
            importInteractive 
                ? "각 패키지마다 임포트할 항목을 선택할 수 있습니다." 
                : "모든 패키지를 자동으로 임포트합니다.",
            MessageType.Info
        );

        GUILayout.Space(10);

        // 임포트 진행 상태 표시
        if (isImporting)
        {
            EditorGUILayout.HelpBox(
                $"임포트 진행 중... ({currentImportIndex + 1}/{packagePaths.Count})\n" +
                $"현재: {Path.GetFileName(packagePaths[currentImportIndex])}\n" +
                (importInteractive ? "임포트 다이얼로그에서 항목을 선택하고 Import를 눌러주세요." : ""),
                MessageType.Warning
            );
            GUILayout.Space(10);
        }

        // 패키지 목록
        GUILayout.Label("Package List:", EditorStyles.boldLabel);
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
        for (int i = 0; i < packagePaths.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(30));
            EditorGUILayout.TextField(packagePaths[i]);
            if (GUILayout.Button("X", GUILayout.Width(30)))
            {
                packagePaths.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUILayout.Space(10);

        // 버튼들
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Package"))
        {
            string path = EditorUtility.OpenFilePanel("Select Unity Package", "", "unitypackage");
            if (!string.IsNullOrEmpty(path) && !packagePaths.Contains(path))
            {
                packagePaths.Add(path);
            }
        }

        if (GUILayout.Button("Add Folder"))
        {
            string folderPath = EditorUtility.OpenFolderPanel("Select Folder with Packages", "", "");
            if (!string.IsNullOrEmpty(folderPath))
            {
                string[] files = Directory.GetFiles(folderPath, "*.unitypackage", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    if (!packagePaths.Contains(file))
                    {
                        packagePaths.Add(file);
                    }
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear List"))
        {
            packagePaths.Clear();
        }

        GUI.enabled = packagePaths.Count > 0 && !isImporting;
        if (GUILayout.Button("Import All", GUILayout.Height(30)))
        {
            StartImportSequence();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label($"Total Packages: {packagePaths.Count}", EditorStyles.miniLabel);
    }

    private void StartImportSequence()
    {
        if (packagePaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Packages", "패키지 목록이 비어있습니다.", "OK");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Confirm Import",
            $"{packagePaths.Count}개의 패키지를 임포트하시겠습니까?" +
            (importInteractive ? "\n\n각 패키지마다 임포트 다이얼로그가 순차적으로 표시됩니다." : ""),
            "Import",
            "Cancel"
        );

        if (!confirm) return;

        // 초기화
        isImporting = true;
        currentImportIndex = 0;
        successCount = 0;
        failCount = 0;

        // 첫 번째 패키지 임포트 시작
        ImportNextPackage();
    }

    private void ImportNextPackage()
    {
        if (currentImportIndex >= packagePaths.Count)
        {
            // 모든 임포트 완료
            FinishImport();
            return;
        }

        string path = packagePaths[currentImportIndex];
        string fileName = Path.GetFileName(path);

        try
        {
            if (File.Exists(path))
            {
                Debug.Log($"[BatchImporter] Importing {currentImportIndex + 1}/{packagePaths.Count}: {fileName}");
                
                if (importInteractive)
                {
                    // 인터랙티브 모드: 다이얼로그가 닫힐 때까지 대기
                    AssetDatabase.ImportPackage(path, true);
                    
                    // AssetDatabase 콜백 등록
                    EditorApplication.update += WaitForImportComplete;
                }
                else
                {
                    // 자동 모드: 바로 임포트
                    AssetDatabase.ImportPackage(path, false);
                    successCount++;
                    currentImportIndex++;
                    
                    // 다음 패키지로 (약간의 딜레이)
                    EditorApplication.delayCall += ImportNextPackage;
                }
            }
            else
            {
                Debug.LogError($"[BatchImporter] File not found: {path}");
                failCount++;
                currentImportIndex++;
                EditorApplication.delayCall += ImportNextPackage;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BatchImporter] Failed to import {fileName}: {e.Message}");
            failCount++;
            currentImportIndex++;
            EditorApplication.delayCall += ImportNextPackage;
        }

        Repaint();
    }

    private bool lastImportDialogState = false;
    private void WaitForImportComplete()
    {
        // 임포트 다이얼로그가 열려있는지 확인
        bool isDialogOpen = EditorWindow.HasOpenInstances<PackageImport>();
        
        if (lastImportDialogState && !isDialogOpen)
        {
            // 다이얼로그가 닫혔음 (임포트 완료 또는 취소)
            EditorApplication.update -= WaitForImportComplete;
            
            successCount++;
            currentImportIndex++;
            lastImportDialogState = false;
            
            Debug.Log($"[BatchImporter] Package import dialog closed. Moving to next package.");
            
            // 다음 패키지로
            EditorApplication.delayCall += ImportNextPackage;
            Repaint();
        }
        
        lastImportDialogState = isDialogOpen;
    }

    private void FinishImport()
    {
        isImporting = false;
        
        string message = $"임포트 완료!\n성공: {successCount}\n실패: {failCount}";
        EditorUtility.DisplayDialog("Import Complete", message, "OK");
        
        AssetDatabase.Refresh();
        Repaint();
        
        Debug.Log($"[BatchImporter] All imports completed. Success: {successCount}, Failed: {failCount}");
    }
}

// 코드 없이 메뉴에서 직접 실행하는 간단한 버전
public static class QuickPackageImporter
{
    [MenuItem("Tools/Quick Import Packages from Folder")]
    public static void QuickImportFromFolder()
    {
        string folderPath = EditorUtility.OpenFolderPanel("Select Folder with Unity Packages", "", "");
        
        if (string.IsNullOrEmpty(folderPath)) return;

        string[] packageFiles = Directory.GetFiles(folderPath, "*.unitypackage", SearchOption.AllDirectories);

        if (packageFiles.Length == 0)
        {
            EditorUtility.DisplayDialog("No Packages Found", "선택한 폴더에 .unitypackage 파일이 없습니다.", "OK");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Confirm Import",
            $"{packageFiles.Length}개의 패키지를 찾았습니다. 모두 임포트하시겠습니까?",
            "Import All",
            "Cancel"
        );

        if (!confirm) return;

        for (int i = 0; i < packageFiles.Length; i++)
        {
            EditorUtility.DisplayProgressBar(
                "Importing Packages",
                $"Importing {Path.GetFileName(packageFiles[i])} ({i + 1}/{packageFiles.Length})",
                (float)i / packageFiles.Length
            );

            AssetDatabase.ImportPackage(packageFiles[i], false);
        }

        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog("Complete", $"{packageFiles.Length}개의 패키지를 임포트했습니다.", "OK");
        AssetDatabase.Refresh();
    }
}