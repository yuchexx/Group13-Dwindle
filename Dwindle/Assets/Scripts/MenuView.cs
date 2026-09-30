using TMPro;
using UnityEngine;
using UnityEngine.UI;

// the start screen, the rules pop-up and the level menu
public class MenuView : MonoBehaviour
{
    [Header("Screens")]
    public GameObject startScreen;
    public GameObject gameScreen;
    public GameObject rulesPopup;
    public GameObject levelMenu;

    [Header("Level menu")]
    public TMP_Text menuTitle;
    public TMP_Text menuSummary;
    public Image[] menuStars;
    public GameObject resumeButton;
    public Button nextButton;

    [Header("Colors")]
    public Color starOnColor = new Color(1f, 0.82f, 0.2f);
    public Color starOffColor = new Color(0.32f, 0.33f, 0.37f);
    public Color bonusStarOnColor = new Color(0.45f, 0.80f, 0.95f); // the third star, for the bonus

    public void ShowStart()
    {
        CloseOverlays();
        startScreen.SetActive(true);
        gameScreen.SetActive(false);
    }

    public void ShowGame()
    {
        CloseOverlays();
        startScreen.SetActive(false);
        gameScreen.SetActive(true);
    }

    public void CloseOverlays()
    {
        rulesPopup.SetActive(false);
        levelMenu.SetActive(false);
    }

    public void SetRulesVisible(bool visible) => rulesPopup.SetActive(visible);

    public void HideMenu() => levelMenu.SetActive(false);

    // the first two stars are delivery tiers; the third is the bonus goal in its own color
    public void ShowMenu(string title, string summary, bool star1, bool star2, bool bonusStar,
                         bool showResume, bool showNext)
    {
        if (menuStars.Length > 0) menuStars[0].color = star1 ? starOnColor : starOffColor;
        if (menuStars.Length > 1) menuStars[1].color = star2 ? starOnColor : starOffColor;
        if (menuStars.Length > 2) menuStars[2].color = bonusStar ? bonusStarOnColor : starOffColor;

        menuTitle.text = title;
        menuSummary.text = summary;

        resumeButton.SetActive(showResume);
        nextButton.gameObject.SetActive(showNext);
        levelMenu.SetActive(true);
    }
}
