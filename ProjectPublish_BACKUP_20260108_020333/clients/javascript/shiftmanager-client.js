/**
 * ShiftManager API JavaScript/TypeScript Client
 *
 * A comprehensive client for the ShiftManager API.
 *
 * Usage (Node.js):
 *   const { ShiftManagerClient } = require('./shiftmanager-client');
 *
 *   const client = new ShiftManagerClient({
 *     baseUrl: 'https://api.example.com',
 *     apiKey: 'your-api-key-here'
 *   });
 *
 *   // List swap requests
 *   const swaps = await client.swapRequests.list({ status: 'Pending' });
 *
 * Usage (Browser):
 *   <script src="shiftmanager-client.js"></script>
 *   <script>
 *     const client = new ShiftManagerClient({
 *       baseUrl: 'https://api.example.com',
 *       apiKey: 'your-api-key-here'
 *     });
 *   </script>
 */

class APIError extends Error {
  constructor(statusCode, detail, response = {}) {
    super(`API Error ${statusCode}: ${detail}`);
    this.name = 'APIError';
    this.statusCode = statusCode;
    this.detail = detail;
    this.response = response;
  }
}

class APIClient {
  constructor(baseUrl, apiKey) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.apiKey = apiKey;
  }

  async request(method, endpoint, options = {}) {
    const url = `${this.baseUrl}${endpoint}`;
    const headers = {
      'X-API-Key': this.apiKey,
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      ...options.headers
    };

    const config = {
      method,
      headers,
      ...options
    };

    if (options.body) {
      config.body = JSON.stringify(options.body);
    }

    if (options.params) {
      const params = new URLSearchParams();
      Object.entries(options.params).forEach(([key, value]) => {
        if (value !== null && value !== undefined) {
          params.append(key, value);
        }
      });
      const queryString = params.toString();
      if (queryString) {
        config.url = `${url}?${queryString}`;
      }
    }

    try {
      const response = await fetch(config.url || url, config);

      // Handle error responses
      if (response.status >= 400) {
        let errorData = {};
        try {
          errorData = await response.json();
        } catch (e) {
          errorData = { detail: await response.text() };
        }

        throw new APIError(
          response.status,
          errorData.detail || response.statusText,
          errorData
        );
      }

      // Handle 204 No Content
      if (response.status === 204) {
        return null;
      }

      // Parse JSON response
      return await response.json();
    } catch (error) {
      if (error instanceof APIError) {
        throw error;
      }
      throw new APIError(0, `Network error: ${error.message}`);
    }
  }

  get(endpoint, params) {
    return this.request('GET', endpoint, { params });
  }

  post(endpoint, body) {
    return this.request('POST', endpoint, { body });
  }

  patch(endpoint, body) {
    return this.request('PATCH', endpoint, { body });
  }

  delete(endpoint) {
    return this.request('DELETE', endpoint);
  }
}

class SwapRequestsResource {
  constructor(client) {
    this.client = client;
  }

  list({ page = 1, pageSize = 50, userId, status, startDate, endDate, includeRelated = false } = {}) {
    return this.client.get('/api/v1/swap-requests', {
      page,
      pageSize,
      userId,
      status,
      startDate,
      endDate,
      includeRelated
    });
  }

  get(swapId, { includeRelated = true } = {}) {
    return this.client.get(`/api/v1/swap-requests/${swapId}`, { includeRelated });
  }

  create({ fromAssignmentId, toAssignmentId, toUserId, reason }) {
    return this.client.post('/api/v1/swap-requests', {
      fromAssignmentId,
      toAssignmentId,
      toUserId,
      reason
    });
  }

  approve(swapId) {
    return this.client.post(`/api/v1/swap-requests/${swapId}/approve`);
  }

  decline(swapId, { declineReason } = {}) {
    return this.client.post(`/api/v1/swap-requests/${swapId}/decline`, { declineReason });
  }

  delete(swapId) {
    return this.client.delete(`/api/v1/swap-requests/${swapId}`);
  }
}

class ChoresResource {
  constructor(client) {
    this.client = client;
  }

  list({ page = 1, pageSize = 50, userId, startDate, endDate, includeRelated = false, includeCanceled = false } = {}) {
    return this.client.get('/api/v1/chores', {
      page,
      pageSize,
      userId,
      startDate,
      endDate,
      includeRelated,
      includeCanceled
    });
  }

  get(choreId, { includeRelated = true } = {}) {
    return this.client.get(`/api/v1/chores/${choreId}`, { includeRelated });
  }

  create({ userId, date, title, notes }) {
    return this.client.post('/api/v1/chores', {
      userId,
      date,
      title,
      notes
    });
  }

  update(choreId, { title, notes }) {
    return this.client.patch(`/api/v1/chores/${choreId}`, {
      title,
      notes
    });
  }

  delete(choreId) {
    return this.client.delete(`/api/v1/chores/${choreId}`);
  }
}

class OnDutyResource {
  constructor(client) {
    this.client = client;
  }

  list({ page = 1, pageSize = 50, userId, startDate, endDate, type, includeRelated = false, includeCanceled = false } = {}) {
    return this.client.get('/api/v1/on-duty', {
      page,
      pageSize,
      userId,
      startDate,
      endDate,
      type,
      includeRelated,
      includeCanceled
    });
  }

  get(onDutyId, { includeRelated = true } = {}) {
    return this.client.get(`/api/v1/on-duty/${onDutyId}`, { includeRelated });
  }

  create({ userId, date, type, notes }) {
    return this.client.post('/api/v1/on-duty', {
      userId,
      date,
      type,
      notes
    });
  }

  update(onDutyId, { notes }) {
    return this.client.patch(`/api/v1/on-duty/${onDutyId}`, { notes });
  }

  delete(onDutyId) {
    return this.client.delete(`/api/v1/on-duty/${onDutyId}`);
  }
}

class FeedbackResource {
  constructor(client) {
    this.client = client;
  }

  list({ page = 1, pageSize = 50, submittedBy, type, status, startDate, endDate, includeRelated = false } = {}) {
    return this.client.get('/api/v1/feedback', {
      page,
      pageSize,
      submittedBy,
      type,
      status,
      startDate,
      endDate,
      includeRelated
    });
  }

  get(feedbackId, { includeRelated = true } = {}) {
    return this.client.get(`/api/v1/feedback/${feedbackId}`, { includeRelated });
  }

  create({ type, content, imageFileName }) {
    return this.client.post('/api/v1/feedback', {
      type,
      content,
      imageFileName
    });
  }

  updateStatus(feedbackId, { status }) {
    return this.client.patch(`/api/v1/feedback/${feedbackId}/status`, { status });
  }

  delete(feedbackId) {
    return this.client.delete(`/api/v1/feedback/${feedbackId}`);
  }
}

class ShiftManagerClient {
  constructor({ baseUrl, apiKey }) {
    const client = new APIClient(baseUrl, apiKey);

    this.swapRequests = new SwapRequestsResource(client);
    this.chores = new ChoresResource(client);
    this.onDuty = new OnDutyResource(client);
    this.feedback = new FeedbackResource(client);
  }
}

// Export for Node.js/CommonJS
if (typeof module !== 'undefined' && module.exports) {
  module.exports = { ShiftManagerClient, APIError };
}

// Example usage
async function example() {
  const client = new ShiftManagerClient({
    baseUrl: 'https://api.example.com',
    apiKey: 'your-api-key-here'
  });

  try {
    // Example 1: List pending swap requests
    console.log('=== Listing Pending Swap Requests ===');
    const swaps = await client.swapRequests.list({
      status: 'Pending',
      includeRelated: true,
      pageSize: 10
    });
    console.log(`Found ${swaps.pagination.totalCount} swap requests`);
    swaps.data.forEach(swap => {
      console.log(`  - Swap #${swap.id}: ${swap.fromUser.displayName} → ${swap.toUser.displayName}`);
    });

    // Example 2: Create a new chore
    console.log('\n=== Creating a New Chore ===');
    const chore = await client.chores.create({
      userId: 10,
      date: '2025-11-20',
      title: 'Office cleaning',
      notes: 'Focus on break room'
    });
    console.log(`Created chore #${chore.id}`);

    // Example 3: Create an on-duty assignment
    console.log('\n=== Creating On-Duty Assignment ===');
    const onDuty = await client.onDuty.create({
      userId: 10,
      date: '2025-11-20',
      type: 'Hakam',
      notes: 'Night shift coverage'
    });
    console.log(`Created on-duty assignment #${onDuty.id}`);

    // Example 4: Submit feedback
    console.log('\n=== Submitting Feedback ===');
    const feedback = await client.feedback.create({
      type: 'Suggestion',
      content: 'It would be great to have a mobile app'
    });
    console.log(`Submitted feedback #${feedback.id}`);

    // Example 5: List chores for a specific user
    console.log('\n=== Listing User Chores ===');
    const userChores = await client.chores.list({
      userId: 10,
      startDate: '2025-11-01',
      endDate: '2025-11-30',
      includeRelated: true
    });
    console.log(`Found ${userChores.pagination.totalCount} chores for user`);
    userChores.data.forEach(chore => {
      console.log(`  - ${chore.date}: ${chore.title}`);
    });

  } catch (error) {
    if (error instanceof APIError) {
      console.error(`API Error ${error.statusCode}: ${error.detail}`);
      console.error('Response:', error.response);
    } else {
      console.error('Error:', error.message);
    }
  }
}

// Uncomment to run example
// example();
