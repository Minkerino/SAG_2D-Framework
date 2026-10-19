using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;

public class Player_Movement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Collider2D playerCol;
    [SerializeField] private Rigidbody2D playerRb;
    [SerializeField] private SpriteRenderer playerSr;

    [Header("Parameters: Horizontal Movement")]
    [SerializeField] private float moveSpeed = 10;
    [SerializeField] private float moveSmoothing = 0.1f;

    [Header("Parameters: Jumping & Groundcheck")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float jumpForce = 20;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float groundCheckRadius = 0.5f;
    [SerializeField] private float groundCheckLength = 1.1f;

    [Header("Parameters: Interaction")]
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private float interactionOffsetY = 0;
    [SerializeField] private float interactionRadius = 2.5f;

    // General variables
    private bool enableMovement = true;

    // Horizontal movement variables
    private float inputMoveX;
    private float targetMoveX;
    private float velocityMoveX;

    // Jump variables
    private bool inputJump;
    private bool grounded;
    private float currentCoyoteTime;

    // Interact variables
    private bool inputInteract;
    private GameObject currentInteractable;
    private GameObject previousInteractable;

    // Input Receive Functions
    private void OnMove(InputValue value)
    {
        inputMoveX = value.Get<Vector2>().x;
    }

    private void OnJump(InputValue value)
    {
        if (!enableMovement) return;
        inputJump = true;
    }

    private void OnInteract(InputValue value)
    {
        if (!enableMovement) return;
        inputInteract = true;
    }

    // Private functions
    private void Update()
    {
        if (enableMovement)
        {
            HandleHorizontalMove();
            HandleGroundCast();
            HandleJump();
            HandleInteractCast();
            HandleInteract();
        }
        else
        {
            // Resetting some stuff when disabling player, such as when starting dialogue.
            targetMoveX = 0;
            velocityMoveX = 0;
            playerRb.linearVelocity = new Vector2(0, playerRb.linearVelocity.y);

            currentInteractable = null;
            previousInteractable = null;
        }
    }

    private void HandleHorizontalMove()
    {
        targetMoveX = Mathf.SmoothDamp(targetMoveX, inputMoveX, ref velocityMoveX, moveSmoothing);
        playerRb.linearVelocity = new Vector2(targetMoveX * moveSpeed, playerRb.linearVelocity.y);

        if (targetMoveX > 0)
        {
            playerSr.flipX = false;
        }
        else if (targetMoveX < 0)
        {
            playerSr.flipX = true;
        }
    }

    private void HandleGroundCast()
    {
        RaycastHit2D hit = Physics2D.CircleCast(this.transform.position + (Vector3) playerCol.offset, groundCheckRadius, Vector2.down, groundCheckLength, groundLayer);

        if (hit.collider != null)
        {
            currentCoyoteTime = coyoteTime;
            grounded = true;
        }
        else
        {
            currentCoyoteTime -= Time.deltaTime;

            if (currentCoyoteTime < 0)
            {
                grounded = false;
            }
        }
    }

    private void HandleJump()
    {
        if (inputJump)
        {
            if (grounded)
            {
                playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x, jumpForce);
            }
            
            inputJump = false;
        }
    }

    private void HandleInteractCast()
    {
        RaycastHit2D[] hits = Physics2D.CircleCastAll(this.transform.position + (Vector3) playerCol.offset, interactionRadius, Vector2.up, 0, interactableLayer);

        if (hits.Length > 0)
        {
            GameObject nearestInteractable = null;
            float lowestDistance = Mathf.Infinity;

            for(int i = 0; i < hits.Length; i++)
            {
                float distance = Vector3.Distance(hits[i].transform.position, this.transform.position);

                if (distance < lowestDistance)
                {
                    lowestDistance = distance;
                    nearestInteractable = hits[i].collider.gameObject;
                }
            }
            
            currentInteractable = nearestInteractable;
        }
        else
        {
            currentInteractable = null;
        }

        if (currentInteractable != previousInteractable)
        {
            if (currentInteractable != null && currentInteractable.TryGetComponent<Interactable_Fungus>(out Interactable_Fungus interactableObjectCurrent))
            {
                interactableObjectCurrent.ShowInteractPrompt();
            }

            if (previousInteractable != null && previousInteractable.TryGetComponent<Interactable_Fungus>(out Interactable_Fungus interactableObjectPrevious))
            {
                interactableObjectPrevious.HideInteractPrompt();
            }

            previousInteractable = currentInteractable;
        }
    }

    private void HandleInteract()
    {
        if (inputInteract)
        {
            if (currentInteractable != null && currentInteractable.TryGetComponent<Interactable_Fungus>(out Interactable_Fungus interactableObjectCurrent))
            {
                interactableObjectCurrent.Interact();
            }
            
            inputInteract = false;
        }
    }

    // Public functions
    public void EnableMovement()
    {
        enableMovement = true;
    }

    public IEnumerator EnableMovementWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        enableMovement = true;
    }

    public void DisableMovement()
    {
        enableMovement = false;
    }

    // Editor functions
    private void OnDrawGizmos()
    {
        // Ground Check Gizmo
        Gizmos.color = new Color(0, 0, 1);
        Gizmos.DrawRay(this.transform.position + (Vector3) playerCol.offset, Vector2.down * groundCheckLength);
        Gizmos.DrawWireSphere(this.transform.position + (Vector3) playerCol.offset + (Vector3.down * groundCheckLength), groundCheckRadius);

        // Interaction Gizmo
        Gizmos.color = new Color(1, 1, 0);
        Gizmos.DrawWireSphere(this.transform.position + (Vector3) playerCol.offset + (new Vector3(0, interactionOffsetY, 0)), interactionRadius);
    }
}
