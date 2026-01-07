"""
ShiftManager API Python Client

A comprehensive Python client for the ShiftManager API.

Installation:
    pip install requests

Usage:
    from shiftmanager_client import ShiftManagerClient

    client = ShiftManagerClient(
        base_url="https://api.example.com",
        api_key="your-api-key-here"
    )

    # List swap requests
    swaps = client.swap_requests.list(status="Pending")

    # Create a chore
    chore = client.chores.create(
        user_id=10,
        date="2025-11-15",
        title="Office cleaning"
    )
"""

import requests
from typing import Optional, Dict, List, Any
from datetime import datetime, date


class APIError(Exception):
    """Base exception for API errors"""
    def __init__(self, status_code: int, detail: str, response: Dict[str, Any] = None):
        self.status_code = status_code
        self.detail = detail
        self.response = response or {}
        super().__init__(f"API Error {status_code}: {detail}")


class APIClient:
    """Base API client with common functionality"""

    def __init__(self, base_url: str, api_key: str):
        self.base_url = base_url.rstrip('/')
        self.api_key = api_key
        self.session = requests.Session()
        self.session.headers.update({
            'X-API-Key': api_key,
            'Content-Type': 'application/json',
            'Accept': 'application/json'
        })

    def _request(self, method: str, endpoint: str, **kwargs) -> Any:
        """Make an HTTP request and handle errors"""
        url = f"{self.base_url}{endpoint}"

        try:
            response = self.session.request(method, url, **kwargs)

            # Handle error responses
            if response.status_code >= 400:
                try:
                    error_data = response.json()
                    detail = error_data.get('detail', response.text)
                except:
                    detail = response.text

                raise APIError(response.status_code, detail, error_data if 'error_data' in locals() else None)

            # Handle 204 No Content
            if response.status_code == 204:
                return None

            # Parse JSON response
            return response.json()

        except requests.RequestException as e:
            raise APIError(0, f"Network error: {str(e)}")

    def _get(self, endpoint: str, params: Optional[Dict] = None) -> Any:
        return self._request('GET', endpoint, params=params)

    def _post(self, endpoint: str, data: Optional[Dict] = None) -> Any:
        return self._request('POST', endpoint, json=data)

    def _patch(self, endpoint: str, data: Optional[Dict] = None) -> Any:
        return self._request('PATCH', endpoint, json=data)

    def _delete(self, endpoint: str) -> None:
        return self._request('DELETE', endpoint)


class SwapRequestsResource:
    """Swap Requests API resource"""

    def __init__(self, client: APIClient):
        self.client = client

    def list(self,
             page: int = 1,
             page_size: int = 50,
             user_id: Optional[int] = None,
             status: Optional[str] = None,
             start_date: Optional[str] = None,
             end_date: Optional[str] = None,
             include_related: bool = False) -> Dict[str, Any]:
        """List swap requests with pagination and filtering"""
        params = {
            'page': page,
            'pageSize': page_size,
            'includeRelated': include_related
        }
        if user_id:
            params['userId'] = user_id
        if status:
            params['status'] = status
        if start_date:
            params['startDate'] = start_date
        if end_date:
            params['endDate'] = end_date

        return self.client._get('/api/v1/swap-requests', params=params)

    def get(self, swap_id: int, include_related: bool = True) -> Dict[str, Any]:
        """Get a single swap request by ID"""
        params = {'includeRelated': include_related}
        return self.client._get(f'/api/v1/swap-requests/{swap_id}', params=params)

    def create(self,
               from_assignment_id: int,
               to_assignment_id: Optional[int] = None,
               to_user_id: Optional[int] = None,
               reason: Optional[str] = None) -> Dict[str, Any]:
        """Create a new swap request"""
        data = {'fromAssignmentId': from_assignment_id}
        if to_assignment_id:
            data['toAssignmentId'] = to_assignment_id
        if to_user_id:
            data['toUserId'] = to_user_id
        if reason:
            data['reason'] = reason

        return self.client._post('/api/v1/swap-requests', data=data)

    def approve(self, swap_id: int) -> Dict[str, Any]:
        """Approve a swap request (admin operation)"""
        return self.client._post(f'/api/v1/swap-requests/{swap_id}/approve')

    def decline(self, swap_id: int, decline_reason: Optional[str] = None) -> Dict[str, Any]:
        """Decline a swap request (admin operation)"""
        data = {}
        if decline_reason:
            data['declineReason'] = decline_reason

        return self.client._post(f'/api/v1/swap-requests/{swap_id}/decline', data=data)

    def delete(self, swap_id: int) -> None:
        """Delete (cancel) a swap request"""
        return self.client._delete(f'/api/v1/swap-requests/{swap_id}')


class ChoresResource:
    """Chores API resource"""

    def __init__(self, client: APIClient):
        self.client = client

    def list(self,
             page: int = 1,
             page_size: int = 50,
             user_id: Optional[int] = None,
             start_date: Optional[str] = None,
             end_date: Optional[str] = None,
             include_related: bool = False,
             include_canceled: bool = False) -> Dict[str, Any]:
        """List chores with pagination and filtering"""
        params = {
            'page': page,
            'pageSize': page_size,
            'includeRelated': include_related,
            'includeCanceled': include_canceled
        }
        if user_id:
            params['userId'] = user_id
        if start_date:
            params['startDate'] = start_date
        if end_date:
            params['endDate'] = end_date

        return self.client._get('/api/v1/chores', params=params)

    def get(self, chore_id: int, include_related: bool = True) -> Dict[str, Any]:
        """Get a single chore by ID"""
        params = {'includeRelated': include_related}
        return self.client._get(f'/api/v1/chores/{chore_id}', params=params)

    def create(self,
               user_id: int,
               date: str,
               title: str,
               notes: Optional[str] = None) -> Dict[str, Any]:
        """Create a new chore"""
        data = {
            'userId': user_id,
            'date': date,
            'title': title
        }
        if notes:
            data['notes'] = notes

        return self.client._post('/api/v1/chores', data=data)

    def update(self,
               chore_id: int,
               title: Optional[str] = None,
               notes: Optional[str] = None) -> Dict[str, Any]:
        """Update a chore"""
        data = {}
        if title:
            data['title'] = title
        if notes is not None:
            data['notes'] = notes

        return self.client._patch(f'/api/v1/chores/{chore_id}', data=data)

    def delete(self, chore_id: int) -> None:
        """Delete (cancel) a chore"""
        return self.client._delete(f'/api/v1/chores/{chore_id}')


class OnDutyResource:
    """On-Duty API resource"""

    def __init__(self, client: APIClient):
        self.client = client

    def list(self,
             page: int = 1,
             page_size: int = 50,
             user_id: Optional[int] = None,
             start_date: Optional[str] = None,
             end_date: Optional[str] = None,
             type: Optional[str] = None,
             include_related: bool = False,
             include_canceled: bool = False) -> Dict[str, Any]:
        """List on-duty assignments with pagination and filtering"""
        params = {
            'page': page,
            'pageSize': page_size,
            'includeRelated': include_related,
            'includeCanceled': include_canceled
        }
        if user_id:
            params['userId'] = user_id
        if start_date:
            params['startDate'] = start_date
        if end_date:
            params['endDate'] = end_date
        if type:
            params['type'] = type

        return self.client._get('/api/v1/on-duty', params=params)

    def get(self, on_duty_id: int, include_related: bool = True) -> Dict[str, Any]:
        """Get a single on-duty assignment by ID"""
        params = {'includeRelated': include_related}
        return self.client._get(f'/api/v1/on-duty/{on_duty_id}', params=params)

    def create(self,
               user_id: int,
               date: str,
               type: str,
               notes: Optional[str] = None) -> Dict[str, Any]:
        """Create a new on-duty assignment"""
        data = {
            'userId': user_id,
            'date': date,
            'type': type
        }
        if notes:
            data['notes'] = notes

        return self.client._post('/api/v1/on-duty', data=data)

    def update(self, on_duty_id: int, notes: Optional[str] = None) -> Dict[str, Any]:
        """Update an on-duty assignment"""
        data = {}
        if notes is not None:
            data['notes'] = notes

        return self.client._patch(f'/api/v1/on-duty/{on_duty_id}', data=data)

    def delete(self, on_duty_id: int) -> None:
        """Delete (cancel) an on-duty assignment"""
        return self.client._delete(f'/api/v1/on-duty/{on_duty_id}')


class FeedbackResource:
    """Feedback API resource"""

    def __init__(self, client: APIClient):
        self.client = client

    def list(self,
             page: int = 1,
             page_size: int = 50,
             submitted_by: Optional[int] = None,
             type: Optional[str] = None,
             status: Optional[str] = None,
             start_date: Optional[str] = None,
             end_date: Optional[str] = None,
             include_related: bool = False) -> Dict[str, Any]:
        """List feedback with pagination and filtering"""
        params = {
            'page': page,
            'pageSize': page_size,
            'includeRelated': include_related
        }
        if submitted_by:
            params['submittedBy'] = submitted_by
        if type:
            params['type'] = type
        if status:
            params['status'] = status
        if start_date:
            params['startDate'] = start_date
        if end_date:
            params['endDate'] = end_date

        return self.client._get('/api/v1/feedback', params=params)

    def get(self, feedback_id: int, include_related: bool = True) -> Dict[str, Any]:
        """Get a single feedback by ID"""
        params = {'includeRelated': include_related}
        return self.client._get(f'/api/v1/feedback/{feedback_id}', params=params)

    def create(self,
               type: str,
               content: str,
               image_filename: Optional[str] = None) -> Dict[str, Any]:
        """Create new feedback"""
        data = {
            'type': type,
            'content': content
        }
        if image_filename:
            data['imageFileName'] = image_filename

        return self.client._post('/api/v1/feedback', data=data)

    def update_status(self, feedback_id: int, status: str) -> Dict[str, Any]:
        """Update feedback status"""
        data = {'status': status}
        return self.client._patch(f'/api/v1/feedback/{feedback_id}/status', data=data)

    def delete(self, feedback_id: int) -> None:
        """Delete feedback"""
        return self.client._delete(f'/api/v1/feedback/{feedback_id}')


class ShiftManagerClient:
    """Main ShiftManager API client"""

    def __init__(self, base_url: str, api_key: str):
        """
        Initialize the ShiftManager API client

        Args:
            base_url: Base URL of the API (e.g., "https://api.example.com")
            api_key: Your API key for authentication
        """
        self._client = APIClient(base_url, api_key)

        # Initialize resource handlers
        self.swap_requests = SwapRequestsResource(self._client)
        self.chores = ChoresResource(self._client)
        self.on_duty = OnDutyResource(self._client)
        self.feedback = FeedbackResource(self._client)


# Example usage
if __name__ == "__main__":
    # Initialize client
    client = ShiftManagerClient(
        base_url="https://api.example.com",
        api_key="your-api-key-here"
    )

    try:
        # Example 1: List pending swap requests
        print("=== Listing Pending Swap Requests ===")
        swaps = client.swap_requests.list(
            status="Pending",
            include_related=True,
            page_size=10
        )
        print(f"Found {swaps['pagination']['totalCount']} swap requests")
        for swap in swaps['data']:
            print(f"  - Swap #{swap['id']}: {swap['fromUser']['displayName']} → {swap['toUser']['displayName']}")

        # Example 2: Create a new chore
        print("\n=== Creating a New Chore ===")
        chore = client.chores.create(
            user_id=10,
            date="2025-11-20",
            title="Office cleaning",
            notes="Focus on break room"
        )
        print(f"Created chore #{chore['id']}")

        # Example 3: Create an on-duty assignment
        print("\n=== Creating On-Duty Assignment ===")
        on_duty = client.on_duty.create(
            user_id=10,
            date="2025-11-20",
            type="Hakam",
            notes="Night shift coverage"
        )
        print(f"Created on-duty assignment #{on_duty['id']}")

        # Example 4: Submit feedback
        print("\n=== Submitting Feedback ===")
        feedback = client.feedback.create(
            type="Suggestion",
            content="It would be great to have a mobile app"
        )
        print(f"Submitted feedback #{feedback['id']}")

        # Example 5: List chores for a specific user
        print("\n=== Listing User Chores ===")
        user_chores = client.chores.list(
            user_id=10,
            start_date="2025-11-01",
            end_date="2025-11-30",
            include_related=True
        )
        print(f"Found {user_chores['pagination']['totalCount']} chores for user")
        for chore in user_chores['data']:
            print(f"  - {chore['date']}: {chore['title']}")

    except APIError as e:
        print(f"API Error: {e}")
        print(f"Details: {e.response}")
