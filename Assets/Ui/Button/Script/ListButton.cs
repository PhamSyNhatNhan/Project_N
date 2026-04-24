using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ListButton : MonoBehaviour
{
    [SerializeField] private GameObject startButton;
    [SerializeField] private GameObject continueButton;
    [SerializeField] private GameObject quitButton;
    [SerializeField] private GameObject listButton;

    private void Start()
    {
        InstanceButtons();
    }

    private void InstanceButtons()
    {
        bool canContinue = System.IO.File.Exists(
            System.IO.Path.Combine(Application.persistentDataPath, "RunSave.json")
        );

        List<GameObject> buttons = new List<GameObject> { startButton, quitButton };
        if (canContinue)
            buttons.Insert(1, continueButton);

        float buttonWidth = startButton.GetComponent<RectTransform>().rect.width;
        float spacing     = 20f;
        float startX      = -(buttons.Count - 1) * (buttonWidth + spacing) / 2;

        for (int i = 0; i < buttons.Count; i++)
        {
            GameObject    btn      = Instantiate(buttons[i], listButton.transform);
            RectTransform rt       = btn.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(buttonWidth, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(startX + i * (buttonWidth + spacing), 0);

            Button uiButton = btn.GetComponent<Button>();
            if (uiButton == null) continue;

            if (buttons[i] == startButton)
                uiButton.onClick.AddListener(OnStartClicked);
            else if (buttons[i] == continueButton)
                uiButton.onClick.AddListener(OnContinueClicked);
            else if (buttons[i] == quitButton)
                uiButton.onClick.AddListener(OnQuitClicked);
        }
    }

    // ── Buttons ───────────────────────────────────────────────────
    private void OnStartClicked()
    {
        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, "Hall");
    }

    private void OnContinueClicked()
    {
        HallController.ShouldContinue = true;
        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, "Hall");
    }

    private void OnQuitClicked()
    {
        Application.Quit();
    }
}