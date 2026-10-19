using System;
using UnityEngine;

[Serializable]
public class Background
{
    public Transform background;
    public bool tileBackground;
    [Range(-1, 1)] public float parallaxAmountX;
    [Range(-1, 1)] public float parallaxAmountY;

    [HideInInspector] public Vector2 startPos;
    [HideInInspector] public float backgroundWidth;
}

public class Background_Parallax : MonoBehaviour
{
    [SerializeField] private Background[] backgrounds;

    private Transform cam;

    private void Start()
    {
        cam = Camera.main.transform;
        SpawnAdditionalBackgrounds();
        SetBackgroundInitialPositionAndWidth();
    }

    private void Update()
    {
        for (int i = 0; i < backgrounds.Length; i++)
        {
            // Parallax movement.
            float distanceX = cam.position.x * backgrounds[i].parallaxAmountX;
            float distanceY = cam.position.y * backgrounds[i].parallaxAmountY;
            float movementX = cam.position.x * (1 - backgrounds[i].parallaxAmountX);

            Vector2 parallaxPosition = new Vector2(backgrounds[i].startPos.x + distanceX, backgrounds[i].startPos.y + distanceY);
            backgrounds[i].background.position = parallaxPosition;

            // Reposition backgrounds for infinite horizontal scrolling when one screen away.
            if (movementX > backgrounds[i].startPos.x + backgrounds[i].backgroundWidth)
            {
                backgrounds[i].startPos.x += backgrounds[i].backgroundWidth;
            }
            else if (movementX < backgrounds[i].startPos.x - backgrounds[i].backgroundWidth)
            {
                backgrounds[i].startPos.x -= backgrounds[i].backgroundWidth;
            }
        }
    }

    private void SpawnAdditionalBackgrounds()
    {
        for (int i = 0; i < backgrounds.Length; i++)
        {
            if (backgrounds[i].tileBackground)
            {
                SpriteRenderer currentBG = backgrounds[i].background.GetComponent<SpriteRenderer>();

                // Spawn additional background to the left.
                Vector2 positionLeft = new Vector2(currentBG.bounds.min.x * 2, backgrounds[i].background.position.y);
                Transform leftBG = Instantiate(backgrounds[i].background, positionLeft, Quaternion.identity, backgrounds[i].background);
                leftBG.localScale = Vector3.one;

                // Spawn additional background to the right.
                Vector2 positionRight = new Vector2(currentBG.bounds.max.x * 2, backgrounds[i].background.position.y);
                Transform rightBG = Instantiate(backgrounds[i].background, positionRight, Quaternion.identity, backgrounds[i].background);
                rightBG.localScale = Vector3.one;
            }
        }
    }

    private void SetBackgroundInitialPositionAndWidth()
    {
        // Assign background class's variable at start. (Constructors return 0, so I used Start instead)
        for (int i = 0; i < backgrounds.Length; i++)
        {
            backgrounds[i].startPos = backgrounds[i].background.position;
            if (backgrounds[i].tileBackground) {backgrounds[i].backgroundWidth = backgrounds[i].background.GetComponent<SpriteRenderer>().bounds.size.x;}
        }
    }
}