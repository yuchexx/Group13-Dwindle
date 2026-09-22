using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// builds the board for a level, handles clicks, moves objects and applies the rules.
public class GameManager : MonoBehaviour
{
    [Header("Prefabs")]
    public TileView tilePrefab;
    public CellView cellPrefab;

    [Header("Board")]
    public RectTransform cellsRoot;
    public float boardSize = 700f;

    [Header("Colors")]
    public Color cellColor = new Color(0.86f, 0.87f, 0.89f);
    public Color obstacleColor = new Color(0.22f, 0.22f, 0.25f);
    public Color tileColor = new Color(0.28f, 0.31f, 0.38f);

    [Header("Testing")]
    public int firstLevel = 0; // 0 = Level 1

    class Cell
    {
        public CellView view;
        public TileType? tile; // null = empty
        public bool isFixed;
        public bool obstacle;
        public TileView tileView;
    }

    int levelIndex;
    LevelData level;
    Cell[,] cells;
    float cellSize;

    void Start()
    {
        LoadLevel(firstLevel);
    }

    public void LoadLevel(int index)
    {
        levelIndex = index;
        level = Levels.All[index];

        BuildBoard();
    }

    // spawn one Cell prefab per grid square
    void BuildBoard()
    {
        Clear(cellsRoot);

        int w = level.width, h = level.height;
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
    }

    // board position of a grid coordinate relative to the board's center
    Vector2 CellPos(Vector2Int c) =>
        new Vector2((c.x - (level.width - 1) / 2f) * cellSize, (c.y - (level.height - 1) / 2f) * cellSize);

    bool InGrid(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < level.width && c.y < level.height;

    // spawn a Tile prefab inside a cell
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

    void OnCellClicked(Vector2Int p)
    {
        Debug.Log($"Clicked cell {p}");
    }
}
