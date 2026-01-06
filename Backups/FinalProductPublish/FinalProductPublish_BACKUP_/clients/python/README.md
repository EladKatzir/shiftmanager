# ShiftManager Python Client

A Python client library for the ShiftManager API.

## Installation

```bash
pip install requests
```

Then copy `shiftmanager_client.py` to your project.

## Quick Start

```python
from shiftmanager_client import ShiftManagerClient

# Initialize the client
client = ShiftManagerClient(
    base_url="https://your-api-url.com",
    api_key="your-api-key-here"
)

# List pending swap requests
swaps = client.swap_requests.list(status="Pending", include_related=True)
for swap in swaps['data']:
    print(f"Swap #{swap['id']}: {swap['reason']}")

# Create a chore
chore = client.chores.create(
    user_id=10,
    date="2025-11-20",
    title="Office cleaning"
)
print(f"Created chore #{chore['id']}")
```

## API Resources

### Swap Requests

```python
# List swap requests
swaps = client.swap_requests.list(
    page=1,
    page_size=50,
    user_id=10,  # Optional: filter by user
    status="Pending",  # Optional: Pending, Approved, Declined
    start_date="2025-11-01",  # Optional
    end_date="2025-11-30",  # Optional
    include_related=True  # Include user/assignment details
)

# Get a single swap request
swap = client.swap_requests.get(swap_id=1, include_related=True)

# Create a swap request
swap = client.swap_requests.create(
    from_assignment_id=123,
    to_assignment_id=456,  # Optional
    to_user_id=20,  # Optional
    reason="Need to attend family event"
)

# Approve a swap request (requires admin scope)
swap = client.swap_requests.approve(swap_id=1)

# Decline a swap request (requires admin scope)
swap = client.swap_requests.decline(
    swap_id=1,
    decline_reason="Scheduling conflict"
)

# Cancel a swap request
client.swap_requests.delete(swap_id=1)
```

### Chores

```python
# List chores
chores = client.chores.list(
    page=1,
    page_size=50,
    user_id=10,  # Optional
    start_date="2025-11-01",  # Optional
    end_date="2025-11-30",  # Optional
    include_related=True,  # Include user details
    include_canceled=False  # Include canceled chores
)

# Get a single chore
chore = client.chores.get(chore_id=1, include_related=True)

# Create a chore
chore = client.chores.create(
    user_id=10,
    date="2025-11-20",
    title="Office cleaning",
    notes="Focus on break room"  # Optional
)

# Update a chore
chore = client.chores.update(
    chore_id=1,
    title="Deep office cleaning",  # Optional
    notes="Updated notes"  # Optional
)

# Delete a chore
client.chores.delete(chore_id=1)
```

### On-Duty Assignments

```python
# List on-duty assignments
assignments = client.on_duty.list(
    page=1,
    page_size=50,
    user_id=10,  # Optional
    start_date="2025-11-01",  # Optional
    end_date="2025-11-30",  # Optional
    type="Hakam",  # Optional: Hakam or Lead
    include_related=True,
    include_canceled=False
)

# Get a single on-duty assignment
assignment = client.on_duty.get(on_duty_id=1, include_related=True)

# Create an on-duty assignment
assignment = client.on_duty.create(
    user_id=10,
    date="2025-11-20",
    type="Hakam",  # Hakam or Lead
    notes="Night shift coverage"  # Optional
)

# Update an on-duty assignment
assignment = client.on_duty.update(
    on_duty_id=1,
    notes="Updated notes"
)

# Delete an on-duty assignment
client.on_duty.delete(on_duty_id=1)
```

### Feedback

```python
# List feedback
feedback_list = client.feedback.list(
    page=1,
    page_size=50,
    submitted_by=10,  # Optional
    type="Error",  # Optional: Error or Suggestion
    status="New",  # Optional: New or ToWorkOn
    start_date="2025-11-01T00:00:00Z",  # Optional
    end_date="2025-11-30T23:59:59Z",  # Optional
    include_related=True
)

# Get a single feedback
feedback = client.feedback.get(feedback_id=1, include_related=True)

# Submit feedback
feedback = client.feedback.create(
    type="Suggestion",  # Error or Suggestion
    content="It would be great to have a mobile app",
    image_filename="screenshot.png"  # Optional
)

# Update feedback status
feedback = client.feedback.update_status(
    feedback_id=1,
    status="ToWorkOn"  # New or ToWorkOn
)

# Delete feedback
client.feedback.delete(feedback_id=1)
```

## Error Handling

```python
from shiftmanager_client import ShiftManagerClient, APIError

client = ShiftManagerClient(
    base_url="https://api.example.com",
    api_key="your-key"
)

try:
    swap = client.swap_requests.get(swap_id=999)
except APIError as e:
    print(f"Error {e.status_code}: {e.detail}")
    print(f"Full response: {e.response}")
```

## Response Format

All list endpoints return paginated responses:

```python
{
    "data": [
        {/* resource objects */}
    ],
    "pagination": {
        "page": 1,
        "pageSize": 50,
        "totalCount": 125,
        "totalPages": 3
    }
}
```

## Requirements

- Python 3.7+
- `requests` library

## License

See main project license.
