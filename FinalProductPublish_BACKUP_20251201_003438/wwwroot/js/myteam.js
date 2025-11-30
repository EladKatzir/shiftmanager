// My Team Calendars - Frontend Logic
// Handles calendar management, member management, and week view rendering

// State
let currentCalendarId = null;
let currentWeekStart = null;
let calendars = [];
let currentMembers = [];
let availableUsers = [];
let selectedMemberIds = new Set();
let calendarFormMode = 'create'; // 'create' or 'rename'
let renameCalendarId = null;
let deleteCalendarId = null;

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    initializeWeek();
    loadCalendars();
    setupEventListeners();
});

function setupEventListeners() {
    // Week navigation
    document.getElementById('btnPrevWeek').addEventListener('click', () => navigateWeek(-7));
    document.getElementById('btnNextWeek').addEventListener('click', () => navigateWeek(7));
    document.getElementById('btnThisWeek').addEventListener('click', () => {
        currentWeekStart = getThisWeeksSunday();
        loadWeekView();
    });

    // Top bar actions
    document.getElementById('btnSwitchCalendar').addEventListener('click', openSwitchCalendarModal);
    document.getElementById('btnConfigureMembers').addEventListener('click', openConfigureMembersModal);
    document.getElementById('btnNewCalendar').addEventListener('click', openNewCalendarModal);

    // Modal actions
    document.getElementById('btnSaveCalendar').addEventListener('click', saveCalendar);
    document.getElementById('btnSaveMembers').addEventListener('click', saveMembers);
    document.getElementById('btnConfirmDelete').addEventListener('click', confirmDeleteCalendar);

    // Close modals on background click
    document.querySelectorAll('.modal').forEach(modal => {
        modal.addEventListener('click', (e) => {
            if (e.target === modal) {
                closeModal(modal.id);
            }
        });
    });

    // Enter key on calendar name input
    document.getElementById('inputCalendarName').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            saveCalendar();
        }
    });
}

// ===== WEEK MANAGEMENT =====

function initializeWeek() {
    currentWeekStart = getThisWeeksSunday();
}

function getThisWeeksSunday() {
    const today = new Date();
    const day = today.getDay();
    const sunday = new Date(today);
    sunday.setDate(today.getDate() - day);
    return formatDate(sunday);
}

function navigateWeek(days) {
    const date = new Date(currentWeekStart);
    date.setDate(date.getDate() + days);
    currentWeekStart = formatDate(date);
    loadWeekView();
}

function formatDate(date) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
}

function formatWeekRange(startDateStr) {
    const start = new Date(startDateStr);
    const end = new Date(start);
    end.setDate(start.getDate() + 6);

    const dayNames = window.MyTeamLocalization.dayNamesShort;
    const monthNames = window.MyTeamLocalization.monthNames;

    const startDay = dayNames[start.getDay()];
    const endDay = dayNames[end.getDay()];
    const startDate = start.getDate();
    const endDate = end.getDate();
    const month = monthNames[start.getMonth()];
    const year = start.getFullYear();

    return `${startDay} ${startDate} – ${endDay} ${endDate} ${month} ${year}`;
}

// ===== API CALLS =====

async function apiCall(url, options = {}) {
    try {
        console.log('API Call:', url, options);
        const response = await fetch(url, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                ...options.headers
            }
        });

        console.log('API Response:', response.status, response.statusText);

        if (!response.ok) {
            let errorMessage = 'Request failed';
            try {
                const contentType = response.headers.get('content-type');
                if (contentType && contentType.includes('application/json')) {
                    const error = await response.json();
                    errorMessage = error.error || errorMessage;
                } else {
                    errorMessage = `${response.status} ${response.statusText}`;
                }
            } catch (e) {
                errorMessage = `${response.status} ${response.statusText}`;
            }
            throw new Error(errorMessage);
        }

        return await response.json();
    } catch (error) {
        console.error('API Error:', error);
        alert(error.message || 'An error occurred');
        throw error;
    }
}

async function loadCalendars() {
    try {
        calendars = await apiCall('/api/team-calendars');

        if (calendars.length === 0) {
            // No calendars, prompt to create one
            document.getElementById('calendarName').textContent = window.MyTeamLocalization.noCalendars;
            document.getElementById('weekGridContainer').innerHTML = `
                <div class="empty-state">
                    <div class="empty-state-icon">📋</div>
                    <h3>${window.MyTeamLocalization.noTeamCalendars}</h3>
                    <p>${window.MyTeamLocalization.createFirstCalendar}</p>
                    <button class="btn btn-primary" onclick="document.getElementById('btnNewCalendar').click()">
                        ${window.MyTeamLocalization.createCalendar}
                    </button>
                </div>
            `;
            return;
        }

        // Load first calendar by default
        currentCalendarId = calendars[0].id;
        document.getElementById('calendarName').textContent = calendars[0].name;
        loadWeekView();
    } catch (error) {
        console.error('Failed to load calendars:', error);
    }
}

async function loadWeekView() {
    if (!currentCalendarId) return;

    const container = document.getElementById('weekGridContainer');
    container.innerHTML = '<div class="loading"><div class="spinner"></div></div>';

    try {
        const data = await apiCall(`/api/team-calendars/${currentCalendarId}/week?date=${currentWeekStart}`);

        console.log('Week view data:', data);

        // Update week range display
        document.getElementById('weekRange').textContent = formatWeekRange(currentWeekStart);

        if (!data.members || data.members.length === 0) {
            document.getElementById('emptyState').style.display = 'block';
            container.innerHTML = '';
            return;
        }

        document.getElementById('emptyState').style.display = 'none';
        renderWeekGrid(data.members);
    } catch (error) {
        console.error('Failed to load week view:', error);
        container.innerHTML = `<div class="empty-state"><h3>${window.MyTeamLocalization.failedToLoadWeekView}</h3></div>`;
    }
}

function renderWeekGrid(members) {
    const dayNames = window.MyTeamLocalization.dayNames;
    const dayNamesShort = window.MyTeamLocalization.dayNamesShort;

    console.log('Rendering week grid with members:', members);

    // Start with day headers (desktop only)
    let html = `
        <div class="week-view-container">
            <div class="day-headers">
                <div class="day-header">${window.MyTeamLocalization.teamMember}</div>
                ${dayNames.map(day => `<div class="day-header">${day}</div>`).join('')}
            </div>
    `;

    // Render member cards
    members.forEach(member => {
        console.log('Rendering member:', member.displayName, 'days:', member.days);

        // Get initials for avatar
        const initials = getInitials(member.displayName);

        html += `
            <div class="member-card">
                <div class="member-card-content">
                    <!-- Member Info -->
                    <div class="member-info-section">
                        <div class="member-avatar">${escapeHtml(initials)}</div>
                        <div class="member-details">
                            <div class="member-name">${escapeHtml(member.displayName)}</div>
                        </div>
                    </div>

                    <!-- Desktop: Day cells in grid -->
                    <div class="days-grid desktop-only">
        `;

        // Desktop view: days in grid (7 columns)
        member.days.forEach((day, index) => {
            html += renderDayCell(day, false);
        });

        html += `
                    </div>

                    <!-- Mobile: Day cells with labels -->
                    <div class="days-grid mobile-only">
        `;

        // Mobile view: days with labels
        member.days.forEach((day, index) => {
            html += `
                <div class="day-cell-mobile">
                    <div class="day-label-mobile">${dayNamesShort[index]}</div>
                    ${renderDayCell(day, true)}
                </div>
            `;
        });

        html += `
                    </div>
                </div>
            </div>
        `;
    });

    html += `</div>`;

    console.log('Generated HTML:', html.substring(0, 500));
    document.getElementById('weekGridContainer').innerHTML = html;
}

function renderDayCell(day, isMobile) {
    const statusClass = day.type.toLowerCase().replace('_', '-').replace(/\s+/g, '-');
    const hasUrl = day.targetUrl !== null && day.targetUrl !== undefined && day.targetUrl !== '';
    const clickableClass = hasUrl ? 'clickable' : '';
    const onclickAttr = hasUrl ? `onclick="navigateTo('${escapeHtml(day.targetUrl)}')"` : '';

    // Build tooltip text
    const tooltip = buildTooltip(day);
    const titleAttr = tooltip ? `title="${escapeHtml(tooltip)}"` : '';

    // Get icon for status type
    const icon = getStatusIcon(day.type);

    // Translate label
    const translatedLabel = translateStatusLabel(day.label);

    return `
        <div class="day-cell">
            <div class="status-badge ${statusClass} ${clickableClass}" ${onclickAttr} ${titleAttr}>
                <div class="status-badge-content">
                    ${icon ? `<span class="status-icon">${icon}</span>` : ''}
                    <span class="status-label">${escapeHtml(translatedLabel)}</span>
                </div>
                ${day.timeRange ? `<div class="status-time">${escapeHtml(day.timeRange)}</div>` : ''}
            </div>
        </div>
    `;
}

function getStatusIcon(type) {
    const icons = {
        'Vacation': '🧳',
        'VacationPartial': '🧳',
        'AfterPartial': '🌙',
        'OnDuty': '🛡️',
        'Shift': '⏱️',
        'Chore': '🔧',
        'Free': '－'
    };
    return icons[type] || '';
}

function translateStatusLabel(label) {
    // Try to find a translation in the statusLabels map
    if (window.MyTeamLocalization && window.MyTeamLocalization.statusLabels) {
        // Check for exact match
        if (window.MyTeamLocalization.statusLabels[label]) {
            return window.MyTeamLocalization.statusLabels[label];
        }

        // For labels with additional info (e.g., "Shift (Morning)"), extract base type
        const baseLabel = label.split('(')[0].trim();
        if (window.MyTeamLocalization.statusLabels[baseLabel]) {
            // Keep the additional info after translation
            const extraInfo = label.includes('(') ? label.substring(label.indexOf('(')) : '';
            return window.MyTeamLocalization.statusLabels[baseLabel] + (extraInfo ? ' ' + extraInfo : '');
        }
    }

    // Return original label if no translation found
    return label;
}

function buildTooltip(day) {
    const parts = [];

    // Event type and label
    parts.push(`${day.type}: ${day.label}`);

    // Time range if available
    if (day.timeRange) {
        parts.push(`Time: ${day.timeRange}`);
    }

    // Navigation info
    if (day.targetUrl) {
        parts.push(`Click to view: ${day.targetUrl}`);
    } else if (day.type !== 'Free') {
        parts.push('(View-only - no navigation available for your role)');
    }

    return parts.join('\n');
}

function getInitials(name) {
    if (!name) return '?';
    const parts = name.trim().split(/\s+/);
    if (parts.length === 1) {
        return parts[0].substring(0, 2).toUpperCase();
    }
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

function navigateTo(url) {
    if (url) {
        window.location.href = url;
    }
}

// ===== CALENDAR MANAGEMENT =====

function openSwitchCalendarModal() {
    openModal('modalSwitchCalendar');
    renderCalendarList();
}

function renderCalendarList() {
    const container = document.getElementById('calendarListContainer');

    if (calendars.length === 0) {
        container.innerHTML = `<div class="empty-state"><p>${window.MyTeamLocalization.noCalendarsAvailable}</p></div>`;
        return;
    }

    let html = '<div class="calendar-list">';

    calendars.forEach(calendar => {
        const isActive = calendar.id === currentCalendarId;
        html += `
            <div class="calendar-card ${isActive ? 'active' : ''}"
                 onclick="switchToCalendar(${calendar.id})">
                <div class="calendar-info">
                    <h3>${escapeHtml(calendar.name)}</h3>
                    <div class="calendar-meta">${calendar.memberCount} ${window.MyTeamLocalization.members}</div>
                </div>
                <div class="calendar-actions" onclick="event.stopPropagation()">
                    <button class="icon-btn" onclick="openRenameCalendarModal(${calendar.id}, '${escapeHtml(calendar.name)}')"
                            title="${window.MyTeamLocalization.rename}">✏️</button>
                    <button class="icon-btn" onclick="openDeleteCalendarModal(${calendar.id}, '${escapeHtml(calendar.name)}')"
                            title="${window.MyTeamLocalization.delete}">🗑️</button>
                </div>
            </div>
        `;
    });

    html += '</div>';
    container.innerHTML = html;
}

function switchToCalendar(calendarId) {
    currentCalendarId = calendarId;
    const calendar = calendars.find(c => c.id === calendarId);
    if (calendar) {
        document.getElementById('calendarName').textContent = calendar.name;
    }
    closeModal('modalSwitchCalendar');
    loadWeekView();
}

function openNewCalendarModal() {
    calendarFormMode = 'create';
    renameCalendarId = null;
    document.getElementById('calendarFormTitle').textContent = window.MyTeamLocalization.newCalendar;
    document.getElementById('inputCalendarName').value = '';
    openModal('modalCalendarForm');
    document.getElementById('inputCalendarName').focus();
}

function openRenameCalendarModal(calendarId, currentName) {
    calendarFormMode = 'rename';
    renameCalendarId = calendarId;
    document.getElementById('calendarFormTitle').textContent = window.MyTeamLocalization.renameCalendarTitle;
    document.getElementById('inputCalendarName').value = currentName;
    openModal('modalCalendarForm');
    closeModal('modalSwitchCalendar');
    document.getElementById('inputCalendarName').focus();
}

function openDeleteCalendarModal(calendarId, calendarName) {
    deleteCalendarId = calendarId;
    document.getElementById('deleteCalendarName').textContent = calendarName;
    openModal('modalDeleteConfirm');
    closeModal('modalSwitchCalendar');
}

async function saveCalendar() {
    const name = document.getElementById('inputCalendarName').value.trim();

    if (!name) {
        alert(window.MyTeamLocalization.pleaseEnterCalendarName);
        return;
    }

    if (name.length > 60) {
        alert(window.MyTeamLocalization.calendarNameMaxLength);
        return;
    }

    try {
        if (calendarFormMode === 'create') {
            const newCalendar = await apiCall('/api/team-calendars', {
                method: 'POST',
                body: JSON.stringify({ name })
            });

            calendars.push(newCalendar);
            currentCalendarId = newCalendar.id;
            document.getElementById('calendarName').textContent = newCalendar.name;
            closeModal('modalCalendarForm');
            loadWeekView();
        } else {
            await apiCall(`/api/team-calendars/${renameCalendarId}`, {
                method: 'PUT',
                body: JSON.stringify({ name })
            });

            const calendar = calendars.find(c => c.id === renameCalendarId);
            if (calendar) {
                calendar.name = name;
                if (renameCalendarId === currentCalendarId) {
                    document.getElementById('calendarName').textContent = name;
                }
            }

            closeModal('modalCalendarForm');
            alert(window.MyTeamLocalization.calendarRenamedSuccessfully);
        }
    } catch (error) {
        // Error already shown by apiCall
    }
}

async function confirmDeleteCalendar() {
    if (!deleteCalendarId) return;

    try {
        await apiCall(`/api/team-calendars/${deleteCalendarId}`, {
            method: 'DELETE'
        });

        // Remove from list
        calendars = calendars.filter(c => c.id !== deleteCalendarId);

        // If we deleted the current calendar, switch to another
        if (deleteCalendarId === currentCalendarId) {
            if (calendars.length > 0) {
                switchToCalendar(calendars[0].id);
            } else {
                currentCalendarId = null;
                document.getElementById('calendarName').textContent = window.MyTeamLocalization.noCalendars;
                document.getElementById('weekGridContainer').innerHTML = `<div class="empty-state"><h3>${window.MyTeamLocalization.noCalendars}</h3></div>`;
            }
        }

        closeModal('modalDeleteConfirm');
        alert(window.MyTeamLocalization.calendarDeletedSuccessfully);
    } catch (error) {
        // Error already shown by apiCall
    }
}

// ===== MEMBER MANAGEMENT =====

async function openConfigureMembersModal() {
    if (!currentCalendarId) {
        alert(window.MyTeamLocalization.pleaseSelectCalendar);
        return;
    }

    openModal('modalConfigureMembers');
    const container = document.getElementById('memberSelectorContainer');
    container.innerHTML = '<div class="loading"><div class="spinner"></div></div>';

    try {
        const data = await apiCall(`/api/team-calendars/${currentCalendarId}/members`);
        currentMembers = data.currentMembers || [];
        availableUsers = data.availableUsers || [];

        // Initialize selected set with current members
        selectedMemberIds = new Set(currentMembers.map(m => m.id));

        renderMemberSelector();
    } catch (error) {
        container.innerHTML = `<div class="empty-state"><h3>${window.MyTeamLocalization.errorLoadingMembers}</h3></div>`;
    }
}

function renderMemberSelector() {
    const container = document.getElementById('memberSelectorContainer');

    const html = `
        <div class="member-selector">
            <div class="member-pane">
                <div class="pane-header">Current Members (${currentMembers.length})</div>
                <input type="text" class="pane-search" placeholder="Search current members..."
                       oninput="filterMembers('current', this.value)">
                <div class="member-list" id="currentMembersList">
                    ${renderMemberList(currentMembers, true)}
                </div>
            </div>
            <div class="member-pane">
                <div class="pane-header">Available Users (${availableUsers.length})</div>
                <input type="text" class="pane-search" placeholder="Search available users..."
                       oninput="filterMembers('available', this.value)">
                <div class="member-list" id="availableMembersList">
                    ${renderMemberList(availableUsers, false)}
                </div>
            </div>
        </div>
    `;

    container.innerHTML = html;
}

function renderMemberList(members, isCurrent) {
    if (members.length === 0) {
        return `<div style="padding: 1rem; text-align: center; color: var(--muted);">${window.MyTeamLocalization.noUsers}</div>`;
    }

    return members.map(member => {
        const isSelected = selectedMemberIds.has(member.id);
        return `
            <div class="member-item ${isSelected ? 'selected' : ''}" data-member-id="${member.id}">
                <input type="checkbox" class="member-checkbox"
                       ${isSelected ? 'checked' : ''}
                       onchange="toggleMember(${member.id})">
                <div class="member-info">
                    <div class="member-name">${escapeHtml(member.displayName)}</div>
                    <div class="member-email">${escapeHtml(member.email)}</div>
                </div>
            </div>
        `;
    }).join('');
}

function toggleMember(userId) {
    if (selectedMemberIds.has(userId)) {
        selectedMemberIds.delete(userId);
    } else {
        selectedMemberIds.add(userId);
    }

    // Update UI
    renderMemberSelector();
}

function filterMembers(listType, searchTerm) {
    const listId = listType === 'current' ? 'currentMembersList' : 'availableMembersList';
    const list = document.getElementById(listId);
    const items = list.querySelectorAll('.member-item');

    const lowerSearch = searchTerm.toLowerCase();

    items.forEach(item => {
        const name = item.querySelector('.member-name').textContent.toLowerCase();
        const email = item.querySelector('.member-email').textContent.toLowerCase();

        if (name.includes(lowerSearch) || email.includes(lowerSearch)) {
            item.style.display = '';
        } else {
            item.style.display = 'none';
        }
    });
}

async function saveMembers() {
    if (!currentCalendarId) return;

    try {
        await apiCall(`/api/team-calendars/${currentCalendarId}/members`, {
            method: 'PUT',
            body: JSON.stringify({ memberUserIds: Array.from(selectedMemberIds) })
        });

        closeModal('modalConfigureMembers');
        loadWeekView();
        alert(window.MyTeamLocalization.membersUpdatedSuccessfully);
    } catch (error) {
        // Error already shown by apiCall
    }
}

// ===== MODAL HELPERS =====

function openModal(modalId) {
    document.getElementById(modalId).classList.add('active');
}

function closeModal(modalId) {
    document.getElementById(modalId).classList.remove('active');
}

// ===== UTILITY =====

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
