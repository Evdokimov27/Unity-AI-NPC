using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SimplePlayerController : MonoBehaviour
{
	[Header("Movement")]
	public float moveSpeed = 5f;
	public float jumpForce = 5f;
	public float gravity = -9.81f;

	[Header("Mouse Look")]
	public Transform cameraTransform;
	public float mouseSensitivity = 2f;
	public float pitchMin = -60f;
	public float pitchMax = 75f;

	private CharacterController controller;
	private Vector3 velocity;
	private float pitch;

	void Awake()
	{
		controller = GetComponent<CharacterController>();
		if (!cameraTransform) cameraTransform = Camera.main.transform;
	}

	void Update()
	{
		HandleMovement();
		if (Input.GetMouseButton(1)) HandleMouseLook();
	}

	void HandleMovement()
	{
		float h = Input.GetAxis("Horizontal");
		float v = Input.GetAxis("Vertical");

		Vector3 move = transform.right * h + transform.forward * v;
		controller.Move(move * moveSpeed * Time.deltaTime);

		if (controller.isGrounded && velocity.y < 0)
			velocity.y = -2f;

		if (Input.GetButtonDown("Jump") && controller.isGrounded)
			velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);

		velocity.y += gravity * Time.deltaTime;
		controller.Move(velocity * Time.deltaTime);
	}

	void HandleMouseLook()
	{
		float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
		float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

		transform.Rotate(Vector3.up * mouseX);

		pitch -= mouseY;
		pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
		cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
	}
}
