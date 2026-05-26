using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Màn hình Gacha trong Hall.
/// Flow x1:  Pull → spawn 1 card úp → chờ tap/skip → flip → CLOSE
/// Flow x10: Pull → spawn 10 cards → animation vòng tròn → chia grid
///           → chờ tap từng thẻ hoặc SKIP → flip lần lượt → CLOSE
/// </summary>
public class GachaUI : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────
    [Header("Banner List")]
    [SerializeField] private Transform              bannerListContainer;
    [SerializeField] private GameObject             bannerItemPrefab;

    [Header("Banner Detail")]
    [SerializeField] private Image                  bannerPortraitImage;
    [SerializeField] private TextMeshProUGUI        bannerNameText;
    [SerializeField] private TextMeshProUGUI        bannerDescText;
    [SerializeField] private TextMeshProUGUI        pityCountText;

    [Header("Pull Buttons")]
    [SerializeField] private Button                 pullOneButton;
    [SerializeField] private Button                 pullTenButton;
    [SerializeField] private TextMeshProUGUI        pullOneCostText;
    [SerializeField] private TextMeshProUGUI        pullTenCostText;

    [Header("Shard Display")]
    [SerializeField] private TextMeshProUGUI        shardText;
    [SerializeField] private TextMeshProUGUI        charShardText;

    [Header("Result Overlay")]
    [SerializeField] private GameObject             resultOverlay;
    [SerializeField] private CanvasGroup            resultOverlayGroup;

    [Header("Single Result")]
    [SerializeField] private GameObject             singleResult;
    [SerializeField] private Transform              singleCardSlot;   // card spawn ở đây

    [Header("Ten Result")]
    [SerializeField] private GameObject             tenResult;
    [SerializeField] private Transform              tenCircleCenter;  // center cho vòng tròn
    [SerializeField] private Transform              tenGridContainer; // GridLayoutGroup
    [SerializeField] private float                  circleRadius = 120f;

    [Header("Action Button")]
    [SerializeField] private Button                 actionButton;     // dùng chung SKIP/CLOSE
    [SerializeField] private TextMeshProUGUI        actionButtonText;

    [Header("Close")]
    [SerializeField] private Button                 closeButton;

    [Header("Prefabs")]
    [SerializeField] private GameObject             gachaCardPrefab;

    [Header("Settings")]
    [SerializeField] private float circleSpinDuration  = 1f;
    [SerializeField] private float cardAppearDelay     = 0.08f;
    [SerializeField] private float autoFlipDelay       = 0.12f;
    [SerializeField] private float overlayFadeDuration = 0.25f;

    // ── Runtime ───────────────────────────────────────────────────
    private GachaRegistry                _registry;
    private GachaBannerData              _currentBanner;
    private readonly List<GachaBannerItemUI> _bannerItems = new List<GachaBannerItemUI>();

    private GachaCardUI                  _singleCard;
    private readonly List<GachaCardUI>   _tenCards    = new List<GachaCardUI>();
    private int                          _flippedCount = 0;
    private bool                         _isAnimating  = false;

    private enum ResultPhase { None, WaitFlip, AllFlipped }
    private ResultPhase _phase = ResultPhase.None;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        closeButton?.onClick.AddListener(Hide);
        pullOneButton?.onClick.AddListener(() => OnPull(1));
        pullTenButton?.onClick.AddListener(() => OnPull(10));
        actionButton?.onClick.AddListener(OnActionButton);
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────
    public void Show()
    {
        gameObject.SetActive(true);
        _registry = FindObjectOfType<GachaRegistry>();
        LoadBannerList();
        RefreshShardDisplay();
        resultOverlay?.SetActive(false);
    }

    public void Hide()
    {
        StopAllCoroutines();
        gameObject.SetActive(false);
    }

    // ── Banner list ───────────────────────────────────────────────
    private void LoadBannerList()
    {
        foreach (Transform child in bannerListContainer)
            Destroy(child.gameObject);
        _bannerItems.Clear();

        if (_registry == null) return;

        foreach (var banner in _registry.GetAllBanners())
        {
            GameObject go   = Instantiate(bannerItemPrefab, bannerListContainer);
            var        item = go.GetComponent<GachaBannerItemUI>();
            if (item == null) continue;

            int pity = GachaManager.Instance?.GetPity(banner.bannerId) ?? 0;
            item.Setup(banner, pity, OnBannerSelected);
            _bannerItems.Add(item);
        }

        // Chọn banner đầu tiên mặc định
        var banners = _registry.GetAllBanners();
        if (banners.Count > 0)
            SelectBanner(banners[0].bannerId);
    }

    private void OnBannerSelected(string bannerId)
    {
        SelectBanner(bannerId);
    }

    private void SelectBanner(string bannerId)
    {
        if (_registry == null) return;
        _currentBanner = _registry.GetBanner(bannerId);
        if (_currentBanner == null) return;

        // Highlight
        foreach (var item in _bannerItems)
            item.SetSelected(false);
        var selected = _bannerItems.Find(i => true); // find by bannerId
        // Tìm đúng item
        for (int i = 0; i < _bannerItems.Count; i++)
        {
            if (_registry.GetAllBanners()[i].bannerId == bannerId)
            {
                _bannerItems[i].SetSelected(true);
                break;
            }
        }

        // Update detail
        if (bannerPortraitImage != null)
        {
            bannerPortraitImage.sprite  = _currentBanner.portrait;
            bannerPortraitImage.enabled = _currentBanner.portrait != null;
        }
        if (bannerNameText != null) bannerNameText.text = _currentBanner.displayName;
        if (bannerDescText  != null) bannerDescText.text  = _currentBanner.description;

        RefreshPityDisplay();
        RefreshCostDisplay();
    }

    // ── Pull ──────────────────────────────────────────────────────
    private void OnPull(int count)
    {
        if (_currentBanner == null || GachaManager.Instance == null) return;
        if (_isAnimating) return;

        var results = GachaManager.Instance.Pull(_currentBanner, count);
        if (results == null) return; // không đủ shard

        RefreshShardDisplay();
        RefreshPityDisplay();

        // Refresh pity bar trên banner item
        int newPity = GachaManager.Instance.GetPity(_currentBanner.bannerId);
        for (int i = 0; i < _bannerItems.Count; i++)
            if (_registry.GetAllBanners()[i].bannerId == _currentBanner.bannerId)
                _bannerItems[i].RefreshPity(newPity, _currentBanner.pityHardLimit);

        _isAnimating = true;

        // Clear card cũ trước khi fade in
        if (singleCardSlot != null)
            foreach (Transform child in singleCardSlot) Destroy(child.gameObject);
        if (tenGridContainer != null)
            foreach (Transform child in tenGridContainer) Destroy(child.gameObject);
        _singleCard = null;
        _tenCards.Clear();

        // Ẩn button trước khi fade in
        if (actionButton != null) actionButton.gameObject.SetActive(false);

        StartCoroutine(FadeOverlay(true, () =>
        {
            if (count == 1) StartCoroutine(ShowSingleResult(results[0]));
            else            StartCoroutine(ShowTenResult(results));
        }));
    }

    // ── Single result ─────────────────────────────────────────────
    private IEnumerator ShowSingleResult(GachaPullResult result)
    {
        singleResult?.SetActive(true);
        tenResult?.SetActive(false);
        SetActionButton("SKIP");

        // Clear old card
        foreach (Transform child in singleCardSlot) Destroy(child.gameObject);

        // Spawn card
        GameObject go = Instantiate(gachaCardPrefab, singleCardSlot);
        _singleCard = go.GetComponent<GachaCardUI>();

        Sprite icon = LoadResultIcon(result);
        _singleCard.Setup(result, icon, () =>
        {
            _flippedCount++;
            if (_flippedCount >= 1) SetActionButton("CLOSE");
        });

        _flippedCount = 0;
        _phase        = ResultPhase.WaitFlip;
        _isAnimating  = false;
        // Hiện SKIP sau khi fade xong
        if (actionButton != null) actionButton.gameObject.SetActive(true);
        SetActionButton("SKIP");
        yield return null;
    }

    // ── Ten result ────────────────────────────────────────────────
    private IEnumerator ShowTenResult(List<GachaPullResult> results)
    {
        singleResult?.SetActive(false);
        tenResult?.SetActive(true);

        // Clear old cards
        foreach (Transform child in tenGridContainer) Destroy(child.gameObject);
        _tenCards.Clear();
        _flippedCount = 0;

        // Spawn 10 cards at circle center
        var circleCards = new List<GachaCardUI>();
        for (int i = 0; i < results.Count; i++)
        {
            GameObject go   = Instantiate(gachaCardPrefab, tenCircleCenter);
            GachaCardUI card = go.GetComponent<GachaCardUI>();
            Sprite icon = LoadResultIcon(results[i]);
            int capturedIdx = i;
            card.Setup(results[i], icon, () =>
            {
                _flippedCount++;
                if (_flippedCount >= _tenCards.Count)
                    SetActionButton("CLOSE");
            });

            go.GetComponent<Button>()?.onClick.AddListener(() => card.OnTap());
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale    = Vector3.zero;
            circleCards.Add(card);
        }

        // Phase 1: cards fly out to circle positions
        float flyDuration = 0.5f;
        float t = 0f;
        while (t < flyDuration)
        {
            t += Time.deltaTime;
            float prog = Mathf.SmoothStep(0f, 1f, t / flyDuration);
            for (int i = 0; i < circleCards.Count; i++)
            {
                float angle = (i / (float)circleCards.Count) * 2f * Mathf.PI - Mathf.PI / 2f;
                Vector3 target = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * circleRadius;
                circleCards[i].transform.localPosition = Vector3.Lerp(Vector3.zero, target, prog);
                circleCards[i].transform.localScale    = Vector3.Lerp(Vector3.zero, Vector3.one, prog);
            }
            yield return null;
        }

        // Phase 2: spin circle
        float spinElapsed = 0f;
        while (spinElapsed < circleSpinDuration)
        {
            spinElapsed += Time.deltaTime;
            float spinAngle = spinElapsed / circleSpinDuration * 360f * 1.5f;
            for (int i = 0; i < circleCards.Count; i++)
            {
                float baseAngle = (i / (float)circleCards.Count) * 2f * Mathf.PI - Mathf.PI / 2f;
                float a = baseAngle + spinAngle * Mathf.Deg2Rad;
                circleCards[i].transform.localPosition =
                    new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * circleRadius;
            }
            yield return null;
        }

        // Phase 3: burst — move to grid
        // Reparent cards to grid container
        for (int i = 0; i < circleCards.Count; i++)
        {
            var card = circleCards[i];
            card.transform.SetParent(tenGridContainer, true);
            card.transform.localScale = Vector3.zero;
            _tenCards.Add(card);
        }

        // Stagger appear in grid
        for (int i = 0; i < _tenCards.Count; i++)
        {
            var card = _tenCards[i];
            card.transform.localPosition = Vector3.zero; // GridLayoutGroup handles position
            float delay = i * cardAppearDelay;
            StartCoroutine(ScaleIn(card.transform, delay));
        }

        yield return new WaitForSeconds(_tenCards.Count * cardAppearDelay + 0.3f);

        // Giờ mới hiện SKIP
        if (actionButton != null) actionButton.gameObject.SetActive(true);
        SetActionButton("SKIP");

        _phase       = ResultPhase.WaitFlip;
        _isAnimating = false;
    }

    private IEnumerator ScaleIn(Transform t, float delay)
    {
        yield return new WaitForSeconds(delay);
        float elapsed = 0f, dur = 0.25f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            t.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, elapsed / dur);
            yield return null;
        }
        t.localScale = Vector3.one;
    }

    // ── Action button ─────────────────────────────────────────────
    private void OnActionButton()
    {
        if (actionButtonText.text == "CLOSE")
        {
            CloseResult();
            return;
        }

        // SKIP
        if (_singleCard != null && singleResult.activeSelf)
        {
            // x1 skip
            _singleCard.Flip();
        }
        else
        {
            // x10 skip — flip remaining cards lần lượt nhanh
            StartCoroutine(SkipTenFlip());
        }
    }

    private IEnumerator SkipTenFlip()
    {
        actionButton.interactable = false;
        foreach (var card in _tenCards)
        {
            if (!card.IsFlipped)
            {
                card.Flip();
                yield return new WaitForSeconds(autoFlipDelay);
            }
        }
        actionButton.interactable = true;
        SetActionButton("CLOSE");
    }

    private void CloseResult()
    {
        StartCoroutine(FadeOverlay(false, () =>
        {
            if (actionButton != null) actionButton.gameObject.SetActive(true);
            _singleCard = null;
            _tenCards.Clear();
            _phase = ResultPhase.None;
        }));
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void SetActionButton(string label)
    {
        if (actionButtonText != null)
            actionButtonText.text = label;
        if (actionButton != null)
            actionButton.interactable = true;
    }



    private IEnumerator FadeOverlay(bool fadeIn, System.Action onDone = null)
    {
        float duration = overlayFadeDuration;
        float from = fadeIn ? 0f : 1f;
        float to   = fadeIn ? 1f : 0f;

        if (fadeIn)
        {
            resultOverlay?.SetActive(true);
            if (resultOverlayGroup != null) resultOverlayGroup.alpha = 0f;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(from, to, t / duration);
            if (resultOverlayGroup != null) resultOverlayGroup.alpha = alpha;
            yield return null;
        }

        if (resultOverlayGroup != null) resultOverlayGroup.alpha = to;

        if (!fadeIn)
            resultOverlay?.SetActive(false);

        onDone?.Invoke();
    }

    private Sprite LoadResultIcon(GachaPullResult result)
    {
        if (result.type == GachaResultType.Talent)
        {
            var registry = FindObjectOfType<PlayerRegistry>();
            var data     = registry?.GetDisplayData(result.talent);
            // Thử portrait trước, fallback sang avatar
            return data?.portrait ?? data?.avatar;
        }
        // Shard — không cần icon, trả null, frontImage sẽ không hiện
        return null;
    }

    private void RefreshShardDisplay()
    {
        if (UserDataManager.Instance == null) return;
        if (shardText     != null) shardText.text     = $"{UserDataManager.Instance.Shards:N0}";
        if (charShardText != null) charShardText.text = $"{UserDataManager.Instance.CharacterShards:N0}";
    }

    private void RefreshPityDisplay()
    {
        if (_currentBanner == null || pityCountText == null) return;
        int pity = GachaManager.Instance?.GetPity(_currentBanner.bannerId) ?? 0;
        pityCountText.text = $"{pity} / {_currentBanner.pityHardLimit}";
    }



    private void RefreshCostDisplay()
    {
        if (_currentBanner == null) return;
        if (pullOneCostText != null) pullOneCostText.text = $"{_currentBanner.costPerPull:N0}";
        if (pullTenCostText != null) pullTenCostText.text = $"{_currentBanner.costPerPull * 10:N0}";
    }
}