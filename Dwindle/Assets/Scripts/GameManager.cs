using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// builds the board for a level, handles clicks, moves objects and applies the rules
public class GameManager : MonoBehaviour
{
    [Header("Prefabs")]
    public TileView tilePrefab;
    public CellView cellPrefab;
    public TileCard slotPrefab;
    public ObjectView objectPrefab;
    public DestinationView destinationPrefab;
    public TileCard cardPrefab;

    [Header("Board")]
    public RectTransform cellsRoot;
    public RectTransform markersRoot;
    public RectTransform objectsRoot;
    public float boardSize = 700f;
    public float moveSecondsPerCell = 0.2f;

    [Header("Colors")]
    public Color cellColor = new Color(0.86f, 0.87f, 0.89f);
    public Color obstacleColor = new Color(0.22f, 0.22f, 0.25f);
    public Color tileColor = new Color(0.28f, 0.31f, 0.38f);
    public Color slotColor = new Color(0.32f, 0.34f, 0.40f);
    public Color selectedSlotColor = new Color(0.95f, 0.78f, 0.25f);
    public Color flashColor = new Color(1f, 0.45f, 0.45f);
    public Color crossHighlightColor = new Color(0.98f, 0.88f, 0.55f);
    public Color recoverySelectedColor = new Color(0.95f, 0.78f, 0.25f);
    public Color starOnColor = new Color(1f, 0.82f, 0.2f);
    public Color starOffColor = new Color(0.32f, 0.33f, 0.37f);
    public Color bonusStarOnColor = new Color(0.45f, 0.80f, 0.95f);

    [Header("HUD")]
    public RectTransform slotsRoot;
    public float groupGap = 24f;
    public TMP_Text levelTitleText;
    public TMP_Text goalText;
    public TMP_Text selectedText;
    public TMP_Text logText;

    [Header("Cross tile choice")]
    public Button arrowButtonPrefab;
    public RectTransform choicesRoot;

    [Header("Removed tiles and recovery")]
    public RectTransform crossRemovedRoot;
    public RectTransform cornerRemovedRoot;
    public RectTransform straightRemovedRoot;
    public GameObject choiceDimmer;
    public TMP_Text removedHeader;
    public float flashHoldSeconds = 0.4f;
    public float flashFadeSeconds = 1.2f;

    [Header("Screens and menus")]
    public GameObject startScreen;
    public GameObject gameScreen;
    public GameObject rulesPopup;
    public GameObject levelMenu;
    public float endPopupDelay = 1.2f;
    public TMP_Text bonusText;
    public TMP_Text menuTitle;
    public TMP_Text menuSummary;
    public Image[] menuStars;
    public GameObject resumeButton;
    public Button nextButton;

    class Cell
    {
        public CellView view;
        public TileType? tile; // null = empty
        public bool obstacle;
        public TileView tileView;
    }

    class MovingObject
    {
        public ObjectSpec spec;
        public Vector2Int pos;  // last cell it passed through (or its start marker)
        public Dir heading;     // the way it faces
        public bool completed;
        public List<Vector2Int> path = new List<Vector2Int>();
        public ObjectView view;
        public DestinationView dest;
        public bool blocked;
    }

    class RemovedEntry
    {
        public TileType type;
        public string source;
    }

    int levelIndex;
    LevelData level;
    Cell[,] cells;
    float cellSize;
    Dictionary<TileType, int> inventory;
    readonly Dictionary<TileType, TileCard> slots = new Dictionary<TileType, TileCard>();
    TileType? selected;
    bool busy; // true while objects are moving or waiting for a choice
    readonly List<string> log = new List<string>();
    List<MovingObject> objects;
    Dir? pendingDirection;
    List<RemovedEntry> removed;
    TileType? pendingRecovery;
    bool comboAchieved;
    string finalComboNote; // set when the last objects arrive together
    HashSet<TileCategory> usedCategories;
    bool levelOver;

    // level loading and the board
    void Start()
    {
        BuildSlots();
        ShowStartScreen();
    }

    public void LoadLevel(int index)
    {
        StopAllCoroutines();
        Clear(choicesRoot);
        choiceDimmer.SetActive(false);
        rulesPopup.SetActive(false);
        levelMenu.SetActive(false);
        startScreen.SetActive(false);
        gameScreen.SetActive(true);
        levelOver = false;
        usedCategories = new HashSet<TileCategory>();
        levelIndex = index;
        level = Levels.All[index];
        inventory = Tiles.All.ToDictionary(t => t, t => level.inventory.TryGetValue(t, out var c) ? c : 0);
        selected = null;
        busy = false;
        log.Clear();
        removed = new List<RemovedEntry>();
        comboAchieved = false;
        finalComboNote = null;

        BuildBoard();
        Log($"{level.title}: pick a tile, then click an empty cell.");
        RefreshAll();
    }

    void BuildBoard()
    {
        Clear(cellsRoot);
        Clear(markersRoot);
        Clear(objectsRoot);

        int w = level.width, h = level.height;
        // leave room for one ring of cells around the grid, where objects and destinations sit
        cellSize = Mathf.Floor(boardSize / (Mathf.Max(w, h) + 2));

        cells = new Cell[w, h];
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            var p = new Vector2Int(x, y);
            var view = Instantiate(cellPrefab, cellsRoot);
            var rt = (RectTransform)view.transform;
            rt.anchoredPosition = CellPos(p);
            rt.sizeDelta = Vector2.one * (cellSize - 4); // 4px gap between cells
            view.background.color = cellColor;
            view.Init(p, OnCellClicked);
            cells[x, y] = new Cell { view = view };
        }

        foreach (var o in level.obstacles)
        {
            cells[o.x, o.y].obstacle = true;
            cells[o.x, o.y].view.background.color = obstacleColor;
        }

        objects = new List<MovingObject>();
        foreach (var spec in level.objects)
        {
            var dest = Instantiate(destinationPrefab, markersRoot);
            dest.Setup(spec.color, cellSize * 0.62f);
            ((RectTransform)dest.transform).anchoredPosition = CellPos(spec.dest);

            var view = Instantiate(objectPrefab, objectsRoot);
            view.Setup(spec.color, cellSize * 0.55f);
            view.Face(spec.heading);

            objects.Add(new MovingObject { spec = spec, pos = spec.start, heading = spec.heading, view = view, dest = dest });
        }
        LayoutWaitingObjects();
    }

    // board position relative to the board's center of a grid coordinate
    Vector2 CellPos(Vector2Int c) =>
        new Vector2((c.x - (level.width - 1) / 2f) * cellSize, (c.y - (level.height - 1) / 2f) * cellSize);

    bool InGrid(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < level.width && c.y < level.height;

    void SetTile(Vector2Int p, TileType t)
    {
        var cell = cells[p.x, p.y];
        cell.tile = t;
        if (cell.tileView != null) Destroy(cell.tileView.gameObject);
        cell.tileView = Instantiate(tilePrefab, cell.view.transform);
        cell.tileView.Show(t, tileColor);
    }

    static void Clear(Transform root)
    {
        foreach (Transform child in root) Destroy(child.gameObject);
    }

    void BuildSlots()
    {
        Clear(slotsRoot);
        TileCategory? previous = null;
        foreach (var t in Tiles.All)
        {
            var category = Tiles.Category(t);
            if (previous != null && category != previous)
            {
                var gap = new GameObject("Gap", typeof(RectTransform));
                var rt = (RectTransform)gap.transform;
                rt.SetParent(slotsRoot, false);
                rt.sizeDelta = new Vector2(groupGap, 1);
            }
            previous = category;

            var slot = Instantiate(slotPrefab, slotsRoot);
            slot.SetTile(tilePrefab, t, tileColor);
            var type = t; // copy for the click handler below
            slot.button.onClick.AddListener(() => SelectTile(type));
            slots[t] = slot;
        }
    }

    void SelectTile(TileType t)
    {
        if (levelOver) return;
        if (inventory[t] <= 0) { Log($"No {Tiles.Name(t)} tiles left."); return; }
        selected = selected == t ? (TileType?)null : t; // clicking the selected tile again deselects it
        RefreshInventory();
    }

    void OnCellClicked(Vector2Int p)
    {
        if (levelOver) return;
        if (busy) return;
        var cell = cells[p.x, p.y];
        if (cell.obstacle) { Log("That space is blocked."); return; }
        if (cell.tile != null) { Log("There's already a tile there. Placed tiles are permanent."); return; }
        if (selected == null) { Log("Select a tile from the top bar first."); return; }

        var t = selected.Value;
        inventory[t]--;
        SetTile(p, t);
        Log($"Placed {Tiles.Name(t)} at ({p.x},{p.y}).");
        usedCategories.Add(Tiles.Category(t));
        RefreshAll();
        StartCoroutine(ResolveRound());
    }

    // movement after one tile placement
    IEnumerator ResolveRound()
    {
        busy = true;
        var deliveredNow = new List<MovingObject>();
        foreach (var o in objects)
        {
            if (o.completed) continue;
            yield return MoveObject(o);
            LayoutWaitingObjects();
            if (o.completed)
            {
                deliveredNow.Add(o);
                Log($"{o.spec.name} reached its destination!");
                if (level.bonus == BonusGoal.ShortestRoute)
                    Log(TookShortestRoute(o)
                        ? $"{o.spec.name} took a shortest route ({o.path.Count} tiles)."
                        : $"{o.spec.name} took {o.path.Count} tiles; the shortest route is {ShortestRouteCells(o.spec)}.");
            }
        }

        if (deliveredNow.Count > 0) yield return HandleDeliveries(deliveredNow);

        busy = false;
        RefreshAll();
        CheckLevelEnd();
    }

    // moves one object as far as the path goes. Each tile is traversed
    // entry edge -> center -> exit edge, and the object waits on the exit edge
    IEnumerator MoveObject(MovingObject o)
    {
        for (int guard = 0; guard < 500; guard++)
        {
            var next = o.pos + DirUtil.Offset(o.heading);

            if (!InGrid(next))
            {
                if (next == o.spec.dest)
                {
                    yield return Slide(o, CellPos(next));
                    o.pos = next;
                    o.completed = true;
                    o.view.gameObject.SetActive(false);
                    o.dest.MarkDelivered();
                }
                yield break; // leaving the grid anywhere else is a dead end
            }

            var cell = cells[next.x, next.y];
            if (cell.tile == null || !Tiles.Has(cell.tile.Value, DirUtil.Opposite(o.heading)))
                yield break; // the path ends here (for now)

            // entry edge - only differs from where we are on the very first move) - then the center.
            yield return Slide(o, CellPos(next) - (Vector2)DirUtil.Offset(o.heading) * cellSize * 0.5f);
            yield return Slide(o, CellPos(next));
            o.pos = next;
            o.path.Add(next);

            var t = cell.tile.Value;
            if (t == TileType.Cross)
            {
                yield return AskDirection(o);
            }
            else
            {
                o.heading = Tiles.OtherExit(t, DirUtil.Opposite(o.heading));
            }
            o.view.Face(o.heading);
            yield return Slide(o, WaitPos(o));
        }
    }

    // where an object rests - the exit edge of the last tile it passed through or its start marker if it hasn't entered the grid yet
    Vector2 WaitPos(MovingObject o) =>
        InGrid(o.pos) ? CellPos(o.pos) + (Vector2)DirUtil.Offset(o.heading) * cellSize * 0.5f : CellPos(o.pos);

    // several spread each group out along its edge so every object stays visible
    void LayoutWaitingObjects()
    {
        var groups = objects.Where(o => !o.completed)
            .GroupBy(o => Vector2Int.RoundToInt(WaitPos(o) * 2f / cellSize));
        foreach (var group in groups)
        {
            var list = group.ToList();
            Vector2 center = WaitPos(list[0]);
            // moving East/West spread vertically and vice versa
            bool movesHorizontally = list[0].heading == Dir.E || list[0].heading == Dir.W;
            Vector2 along = movesHorizontally ? Vector2.up : Vector2.right;
            float spacing = Mathf.Min(0.36f, 0.9f / list.Count) * cellSize;
            for (int i = 0; i < list.Count; i++)
                list[i].view.Rect.anchoredPosition = center + along * ((i - (list.Count - 1) / 2f) * spacing);
        }
    }

    // animates the object to a board position
    IEnumerator Slide(MovingObject o, Vector2 target)
    {
        Vector2 start = o.view.Rect.anchoredPosition;
        float seconds = moveSecondsPerCell * Vector2.Distance(start, target) / cellSize;
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            o.view.Rect.anchoredPosition = Vector2.Lerp(start, target, t / seconds);
            yield return null;
        }
        o.view.Rect.anchoredPosition = target;
    }

    int Delivered => objects.Count(o => o.completed);

    // object waits at the center of the + tile
    // an arrow button in the object's color appears at each of the tile's other edges but not the one it came in through
    // clicking one sends it out that way
    IEnumerator AskDirection(MovingObject o)
    {
        var cellBg = cells[o.pos.x, o.pos.y].view.background;
        cellBg.color = crossHighlightColor;
        Clear(choicesRoot);

        pendingDirection = null;
        foreach (var d in DirUtil.All)
        {
            if (d == DirUtil.Opposite(o.heading)) continue; // can't turn back
            var dir = d; // copy for the click handler below
            var button = Instantiate(arrowButtonPrefab, choicesRoot);
            var rt = (RectTransform)button.transform;
            rt.sizeDelta = Vector2.one * cellSize * 0.25f;
            rt.anchoredPosition = CellPos(o.pos) + (Vector2)DirUtil.Offset(d) * cellSize * 0.39f; // near that edge
            rt.localEulerAngles = new Vector3(0, 0, DirUtil.Angle(d)); // the prefab's arrow points up
            button.GetComponent<Image>().color = o.spec.color;
            button.onClick.AddListener(() => pendingDirection = dir);
        }

        Log($"{o.spec.name} is on a + tile. Click an arrow to choose its direction.");
        while (pendingDirection == null) yield return null; // wait for a click

        Clear(choicesRoot);
        cellBg.color = cellColor;
        o.heading = pendingDirection.Value;
    }

    // after delivery - tile loss and recovery
    IEnumerator HandleDeliveries(List<MovingObject> deliveredNow)
    {
        var removedNow = new List<RemovedEntry>();
        foreach (var o in deliveredNow)
        {
            // one random tile from the completed path
            var candidates = o.path.Distinct()
                .Where(c => cells[c.x, c.y].tile != null)
                .ToList();
            if (candidates.Count > 0)
            {
                var c = candidates[Random.Range(0, candidates.Count)];
                var entry = new RemovedEntry { type = cells[c.x, c.y].tile.Value, source = $"{o.spec.name}'s path" };
                ClearTile(c);
                removed.Add(entry);
                removedNow.Add(entry);
                Log($"Removed {Tiles.Name(entry.type)} from the board at ({c.x},{c.y}).");
            }

            // one random unused tile from the supply
            var lost = TakeRandomFromSupply();
            if (lost != null)
            {
                var entry = new RemovedEntry { type = lost.Value, source = "unused supply" };
                removed.Add(entry);
                removedNow.Add(entry);
                Log($"Lost an unused {Tiles.Name(entry.type)}.");
            }
        }
        RefreshAll();

        // 2 or more deliveries from one placement - the player gets one removed tile back unless that finished the level
        if (deliveredNow.Count >= 2)
        {
            comboAchieved = true;
            if (objects.All(o => o.completed))
            {
                finalComboNote = string.Join(" and ", deliveredNow.Select(o => o.spec.name)) + " arrived together!";
                Log(finalComboNote);
            }
            else if (removed.Count > 0) yield return RecoveryChoice();
        }
    }

    IEnumerator RecoveryChoice()
    {
        pendingRecovery = null;
        choiceDimmer.SetActive(true);
        removedHeader.text = "Pick one to get back:";
        RefreshRemoved(pickable: true);
        Log("Multiple deliveries with one tile! Pick any tile in the Removed panel to get it back.");

        while (pendingRecovery == null) yield return null; // wait for a click on a card
        yield return new WaitForSeconds(0.25f);

        choiceDimmer.SetActive(false);
        removedHeader.text = "Removed Tiles";

        var picked = removed.First(e => e.type == pendingRecovery.Value);
        inventory[picked.type]++;
        removed.Remove(picked);
        Log($"Recovered {Tiles.Name(picked.type)} to your supply.");
        RefreshAll();
    }


    // picks a random unused tile, weighted by how many of each you hold
    TileType? TakeRandomFromSupply()
    {
        int total = inventory.Values.Sum();
        if (total == 0) return null;
        int r = Random.Range(0, total);
        foreach (var t in Tiles.All)
        {
            if (r < inventory[t]) { inventory[t]--; return t; }
            r -= inventory[t];
        }
        return null;
    }

    void ClearTile(Vector2Int p)
    {
        var cell = cells[p.x, p.y];
        cell.tile = null;
        if (cell.tileView != null) Destroy(cell.tileView.gameObject);
        cell.tileView = null;
        StartCoroutine(Flash(cell.view.background));
    }

    // briefly tints a cell red so the player sees where a tile was removed.
    IEnumerator Flash(Image img)
    {
        img.color = flashColor;
        yield return new WaitForSeconds(flashHoldSeconds);
        for (float t = 0; t < flashFadeSeconds; t += Time.deltaTime)
        {
            img.color = Color.Lerp(flashColor, cellColor, t / flashFadeSeconds);
            yield return null;
        }
        img.color = cellColor;
    }

    // end of level, stars and menus
    void CheckLevelEnd()
    {
        if (levelOver) return;
        UpdateBlocked();
        string reason = null;
        if (objects.All(o => o.completed)) reason = "Every object was delivered!";
        else if (objects.All(o => o.completed || o.blocked)) reason = "Every remaining object is blocked.";
        else if (objects.All(o => o.completed || !CanStillReach(o))) reason = "No remaining object can reach its destination.";
        else if (inventory.Values.Sum() == 0) reason = "You ran out of tiles.";
        else if (!AnyEmptyCell()) reason = "There are no empty cells left.";
        if (reason == null) return;

        levelOver = true;
        selected = null;
        Log(reason);
        RefreshAll();
        StartCoroutine(ShowEndMenuAfterDelay(reason));
    }

    IEnumerator ShowEndMenuAfterDelay(string reason)
    {
        yield return new WaitForSeconds(endPopupDelay);
        ShowMenu(true, reason);
    }

    // blocked = the grid edge, an obstacle, or a tile with no opening toward the object
    // facing an empty cell is never blocked
    bool IsBlocked(MovingObject o)
    {
        var next = o.pos + DirUtil.Offset(o.heading);
        if (!InGrid(next)) return next != o.spec.dest;
        var cell = cells[next.x, next.y];
        if (cell.obstacle) return true;
        return cell.tile != null && !Tiles.Has(cell.tile.Value, DirUtil.Opposite(o.heading));
    }

    // a breadth-first search over every (cell, heading) the object could get to
    bool CanStillReach(MovingObject o)
    {
        var seen = new HashSet<(Vector2Int, Dir)>();
        var queue = new Queue<(Vector2Int pos, Dir heading)>();
        queue.Enqueue((o.pos, o.heading));
        while (queue.Count > 0)
        {
            var (pos, heading) = queue.Dequeue();
            var next = pos + DirUtil.Offset(heading);
            if (!InGrid(next))
            {
                if (next == o.spec.dest) return true;
                continue; // any other edge is a dead end
            }
            var cell = cells[next.x, next.y];
            if (cell.obstacle) continue;
            var entry = DirUtil.Opposite(heading);
            if (cell.tile != null && !Tiles.Has(cell.tile.Value, entry)) continue;

            foreach (var exit in DirUtil.All)
            {
                if (exit == entry) continue;
                if (cell.tile != null && !Tiles.Has(cell.tile.Value, exit)) continue;
                if (seen.Add((next, exit))) queue.Enqueue((next, exit));
            }
        }
        return false;
    }

    // crosses out blocked objects, and logs when one becomes blocked or free again
    void UpdateBlocked()
    {
        foreach (var o in objects.Where(o => !o.completed))
        {
            bool now = IsBlocked(o);
            if (now != o.blocked) Log(now ? $"{o.spec.name} is blocked." : $"{o.spec.name} is free to move again.");
            o.blocked = now;
            o.view.SetBlocked(now);
        }
    }

    bool AnyEmptyCell()
    {
        foreach (var c in cells)
            if (!c.obstacle && c.tile == null) return true;
        return false;
    }

    bool BonusMet()
    {
        switch (level.bonus)
        {
            case BonusGoal.UseEveryType: return usedCategories.Count == 3;
            case BonusGoal.NoCross:      return !usedCategories.Contains(TileCategory.Cross);
            case BonusGoal.ShortestRoute: return objects.All(o => o.completed && TookShortestRoute(o));
            default:                     return comboAchieved;
        }
    }

    bool TookShortestRoute(MovingObject o) => o.path.Count == ShortestRouteCells(o.spec);

    // fewest grid cells any route can use from the object's entry cell to the cell next to its destination, going around obstacles
    int ShortestRouteCells(ObjectSpec s)
    {
        var from = s.start + DirUtil.Offset(s.heading);
        var to = new Vector2Int(Mathf.Clamp(s.dest.x, 0, level.width - 1), Mathf.Clamp(s.dest.y, 0, level.height - 1));
        var dist = new Dictionary<Vector2Int, int> { [from] = 1 };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (c == to) return dist[c];
            foreach (var d in DirUtil.All)
            {
                var n = c + DirUtil.Offset(d);
                if (!InGrid(n) || cells[n.x, n.y].obstacle || dist.ContainsKey(n)) continue;
                dist[n] = dist[c] + 1;
                queue.Enqueue(n);
            }
        }
        return int.MaxValue; // unreachable
    }

    int Stars()
    {
        if (Delivered < level.required) return 0;
        if (Delivered < objects.Count) return 1;
        return BonusMet() ? 3 : 2;
    }

    // menu button (paused) and the end of a level share this pop-up
    void ShowMenu(bool ended, string reason)
    {
        // the first two stars are delivery tiers
        // the third is the bonus goal in its own color
        int stars = Stars();
        if (menuStars.Length > 0) menuStars[0].color = Delivered >= level.required ? starOnColor : starOffColor;
        if (menuStars.Length > 1) menuStars[1].color = Delivered >= objects.Count ? starOnColor : starOffColor;
        if (menuStars.Length > 2) menuStars[2].color = BonusMet() ? bonusStarOnColor : starOffColor;

        bool isLast = levelIndex >= Levels.All.Count - 1;
        menuTitle.text = level.title + (ended ? " complete" : " paused");
        menuSummary.text =
            (reason != null ? reason + "\n" : "") +
            (ended && finalComboNote != null ? finalComboNote + "\n" : "") +
            $"Delivered {Delivered} / {objects.Count} (need {level.required})\n" +
            $"Bonus: {(BonusMet() ? "done" : "not done")}" +
            (stars == 0 ? "\nEarn at least 1 star to unlock the next level." : "") +
            (isLast && stars > 0 ? "\nThat was the last level!" : "");

        resumeButton.SetActive(!ended);
        nextButton.gameObject.SetActive(ended && stars >= 1 && !isLast);
        levelMenu.SetActive(true);
    }

    public void ShowStartScreen()
    {
        StopAllCoroutines();
        rulesPopup.SetActive(false);
        levelMenu.SetActive(false);
        choiceDimmer.SetActive(false);
        startScreen.SetActive(true);
        gameScreen.SetActive(false);
    }

    public void StartGame() => LoadLevel(0);
    public void Restart() => LoadLevel(levelIndex);
    public void NextLevel() { if (levelIndex + 1 < Levels.All.Count) LoadLevel(levelIndex + 1); }
    public void OpenMenu() => ShowMenu(levelOver, null);
    public void Resume() => levelMenu.SetActive(false);
    public void ShowRules() => rulesPopup.SetActive(true);
    public void HideRules() => rulesPopup.SetActive(false);
    // clicking outside the home panel closes it but only while paused not end of the level
    public void DismissMenuIfPaused()
    {
        if (!levelOver) levelMenu.SetActive(false);
    }

    // HUD
    void RefreshAll()
    {
        RefreshInventory();
        RefreshInfo();
        RefreshRemoved();
    }

    void RefreshInventory()
    {
        if (selected != null && inventory[selected.Value] <= 0) selected = null;
        foreach (var t in Tiles.All)
        {
            slots[t].label.text = "x" + inventory[t];
            slots[t].background.color = selected == t ? selectedSlotColor : slotColor;
        }
        selectedText.text = "Selected: " + (selected == null ? "none" : Tiles.Name(selected.Value));
    }

    void RefreshInfo()
    {
        levelTitleText.text = level.title;
        string goal = "";
        goal += "Deliver every object to the ring of its own color.\n\n" +
                $"Delivered: {Delivered} of {objects.Count}\n";
        goal += $"Tiles left: {inventory.Values.Sum()}";
        goal += $"\n\n1 star: deliver {level.required}" +
                $"\n2 stars: deliver all {objects.Count}";
        goalText.text = goal;
        bonusText.text = $"3 stars: deliver all {objects.Count} and\n{level.BonusText()}\n" +
                         $"Bonus so far: {(BonusMet() ? "done" : "not yet")}";
    }

    void RefreshRemoved(bool pickable = false)
    {
        Clear(crossRemovedRoot);
        Clear(cornerRemovedRoot);
        Clear(straightRemovedRoot);
        int crossCards = 0, cornerCards = 0, straightCards = 0;

        foreach (var t in Tiles.All)
        {
            int count = removed.Count(e => e.type == t);
            if (count == 0) continue;
            var category = Tiles.Category(t);
            var parent = category switch
            {
                TileCategory.Cross => crossRemovedRoot,
                TileCategory.Corner => cornerRemovedRoot,
                _ => straightRemovedRoot,
            };
            if (category == TileCategory.Cross) crossCards++;
            else if (category == TileCategory.Corner) cornerCards++;
            else straightCards++;

            var card = Instantiate(cardPrefab, parent);
            card.SetTile(tilePrefab, t, tileColor);
            card.label.text = "x" + count;

            card.button.enabled = pickable; // otherwise the card is just a display
            if (pickable)
            {
                var type = t;       // copies for the click handler below
                var clicked = card;
                card.button.onClick.AddListener(() =>
                {
                    clicked.background.color = recoverySelectedColor; // show what was picked
                    pendingRecovery = type;
                });
            }
        }

        crossRemovedRoot.gameObject.SetActive(crossCards > 0);
        cornerRemovedRoot.gameObject.SetActive(cornerCards > 0);
        straightRemovedRoot.gameObject.SetActive(straightCards > 0);
    }

    void Log(string msg)
    {
        log.Insert(0, "- " + msg); // newest first
        if (log.Count > 12) log.RemoveAt(log.Count - 1);
        logText.text = string.Join("\n", log);
    }
}
