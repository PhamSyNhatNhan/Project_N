using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Prefab 1 item trong banner list bên trái GachaUI.
/// Hiện portrait + tên + pity bar.
/// Tap → callback OnSelected(bannerId).
/// </summary>
[RequireComponent(typeof(Button))]
public class GachaBannerItemUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image           portraitImage;

    // ── Runtime ───────────────────────────────────────────────────
    private string           _bannerId;
    private Action<string>   _onSelected;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnTap);
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void Setup(GachaBannerData data, int currentPity, Action<string> onSelected)
    {
        _bannerId   = data.bannerId;
        _onSelected = onSelected;

        if (portraitImage != null)
        {
            portraitImage.sprite  = data.portrait;
            portraitImage.enabled = data.portrait != null;
        }


        RefreshPity(currentPity, data.pityHardLimit);
        SetSelected(false);
    }

    public void RefreshPity(int pity, int hardLimit) { }

    public void SetSelected(bool selected) { }

    private void OnTap() => _onSelected?.Invoke(_bannerId);
}