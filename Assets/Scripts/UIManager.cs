using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    public GameObject targetPanel;
    private bool isPanelActive;
    public void TogglePanel()
    {
        isPanelActive = !isPanelActive;
        targetPanel.SetActive(isPanelActive);
    }
    public void OpenPanel()
    {
        isPanelActive = true;
        targetPanel.SetActive(true);
    }
    public void ClosePanel()
    {
        isPanelActive = false;
        targetPanel.SetActive(false);
    }
    public void BackToMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }
}
