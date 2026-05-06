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
    document.getElementById('btnPrevWeek')?.addEventListener('click', () => navigateWeek(-7));
    document.getElementById('btnNextWeek')?.addEventListener('click', () => navigateWeek(7));
    document.getElementById('btnThisWeek')?.addEventListener('click', () => {
        currentWeekStart = getThisWeeksSunday();
        loadWeekView();
    });

    // Top bar actions
    document.getElementById('btnSwitchCalendar')?.addEventListener('click', openSwitchCalendarModal);
    document.getElementById('btnConfigureMembers')?.addEventListener('click', openConfigureMembersModal);
    document.getElementById('btnNewCalendar')?.addEventListener('click', openNewCalendarModal);

    // Modal actions
    document.getElementById('btnSaveCalendar')?.addEventListener('click', saveCalendar);
    document.getElementById('btnSaveMembers')?.addEventListener('click', saveMembers);
    document.getElementById('btnConfirmDelete')?.addEventListener('click', confirmDeleteCalendar);

    // Close modals on background click
    document.querySelectorAll('.modal').forEach(modal => {
        modal.addEventListener('click', (e) => {
            if (e.target === modal) {
                closeModal(modal.id);
            }
        });
    });

    // Enter key on calendar name input
    document.getElementById('inputCalendarName')?.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            saveCalendar();
        }
    });
}

// ===== WEEK MANAGEMENT =====

function initializeWeek() {
    currentWeekStart = getThisWeeksSunday();
    const weekRangeEl = document.getElementById('weekRange');
    if (weekRangeEl) weekRangeEl.textContent = formatWeekRange(currentWeekStart);
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

    const loc = window.MyTeamLocalization || {};
    const dayNames = loc.dayNamesShort || ['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
    const monthNames = loc.monthNames || ['January','February','March','April','May','June','July','August','September','October','November','December'];

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
        Logger.log('MyTeam', 'API Call:', url, options);

        const headers = {
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest',
            ...options.headers
        };

        // Add CSRF token for state-changing requests
        const method = (options.method || 'GET').toUpperCase();
        if (method !== 'GET' && method !== 'HEAD') {
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (csrfToken) {
                headers['RequestVerificationToken'] = csrfToken;
            }
        }

        const response = await fetch(url, {
            ...options,
            headers: headers,
            credentials: 'same-origin'
        });

        Logger.log('MyTeam', 'API Response:', response.status, response.statusText);

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
        Logger.error('MyTeam', 'API Error:', error);
        window.FeedbackModal.show('error', error.message || window.AppLocalizer.AnErrorOccurred);
        throw error;
    }
}

async function loadCalendars() {
    try {
        calendars = await apiCall('/api/team-calendars');

        if (calendars.length === 0) {
            // No calendars, prompt to create one
            const calNameEl = document.getElementById('calendarName');
            if (calNameEl) calNameEl.textContent = window.MyTeamLocalization.noCalendars;
            const weekGridEl = document.getElementById('weekGridContainer');
            if (weekGridEl) {
                weekGridEl.innerHTML = `
                    <div class="empty-state">
                        <div class="empty-state-icon">📋</div>
                        <h3>${window.MyTeamLocalization.noTeamCalendars}</h3>
                        <p>${window.MyTeamLocalization.createFirstCalendar}</p>
                        <button class="btn btn-primary" onclick="document.getElementById('btnNewCalendar')?.click()">
                            ${window.MyTeamLocalization.createCalendar}
                        </button>
                    </div>
                `;
            }
            return;
        }

        // Load first calendar by default
        currentCalendarId = calendars[0].id;
        const calNameDisplay = document.getElementById('calendarName');
        if (calNameDisplay) calNameDisplay.textContent = calendars[0].name;
        loadWeekView();
    } catch (error) {
        Logger.error('MyTeam', 'Failed to load calendars:', error);
    }
}

async function loadWeekView() {
    if (!currentCalendarId) return;

    const container = document.getElementById('weekGridContainer');
    if (!container) return;
    container.innerHTML = '<div class="loading"><div class="spinner"></div></div>';

    try {
        const data = await apiCall(`/api/team-calendars/${currentCalendarId}/week?date=${currentWeekStart}`);

        Logger.log('MyTeam', 'Week view data:', data);

        // Update week range display
        const weekRangeEl = document.getElementById('weekRange');
        if (weekRangeEl) weekRangeEl.textContent = formatWeekRange(currentWeekStart);

        if (!data.members || data.members.length === 0) {
            const emptyEl = document.getElementById('emptyState');
            if (emptyEl) emptyEl.style.display = 'block';
            container.innerHTML = '';
            return;
        }

        const emptyStateEl = document.getElementById('emptyState');
        if (emptyStateEl) emptyStateEl.style.display = 'none';
        renderWeekGrid(data.members);
    } catch (error) {
        Logger.error('MyTeam', 'Failed to load week view:', error);
        container.innerHTML = `<div class="empty-state"><h3>${window.MyTeamLocalization.failedToLoadWeekView}</h3></div>`;
    }
}

function renderWeekGrid(members) {
    const dayNames = window.MyTeamLocalization.dayNames;
    const dayNamesShort = window.MyTeamLocalization.dayNamesShort;

    Logger.log('MyTeam', 'Rendering week grid with members:', members);

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
        Logger.log('MyTeam', 'Rendering member:', member.displayName, 'days:', member.days);

        // Get initials for avatar fallback
        const initials = getInitials(member.displayName);
        const avatarContent = member.avatarUrl
            ? `<img src="${escapeHtml(member.avatarUrl)}" alt="${escapeHtml(member.displayName)}" loading="lazy" decoding="async">`
            : escapeHtml(initials);

        html += `
            <div class="member-card">
                <div class="member-card-content">
                    <!-- Member Info -->
                    <div class="member-info-section">
                        <div class="member-avatar">${avatarContent}</div>
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

    Logger.log('MyTeam', 'Generated HTML:', html.substring(0, 500));
    const gridContainer = document.getElementById('weekGridContainer');
    if (gridContainer) gridContainer.innerHTML = html;
}

function renderDayCell(day, isMobile) {
    const statusClass = day.type.toLowerCase().replace('_', '-').replace(/\s+/g, '-');
    const hasUrl = day.targetUrl != null && day.targetUrl !== '';
    const clickableClass = hasUrl ? 'clickable' : '';
    const onclickAttr = hasUrl ? `onclick="navigateTo('${escapeJsAttr(day.targetUrl)}')"` : '';

    // Build tooltip text
    const tooltip = buildTooltip(day);
    const titleAttr = tooltip ? `title="${escapeHtml(tooltip)}"` : '';

    // Get icon for status type. Task 24 — HOME shows source-icon (rotation/vacation/after)
    // + house icon, mirroring the unified chip used on the calendar pages.
    const icon = getStatusIcon(day.type, day.metadata);

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

function getStatusIcon(type, metadata) {
    // Task 24 — HOME chip shows two emoji icons: source (loop/luggage/moon)
    // followed by a house, matching the unified chip on Calendar pages
    // (repeat/plane/sunrise + house Lucide icons there).
    if (type === 'Home') {
        const sourceIcon = metadata === 'vacation' ? '🧳'
                         : metadata === 'after'    ? '🌙'
                         :                            '🔄'; // rotation default
        return sourceIcon + '🏠';
    }

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
        parts.push(`${window.AppLocalizer.Time}: ${day.timeRange}`);
    }

    // Navigation info
    if (day.targetUrl) {
        parts.push(`${window.AppLocalizer.ClickToView}: ${day.targetUrl}`);
    } else if (day.type !== 'Free') {
        parts.push(`(${window.AppLocalizer.ViewOnlyNoNavigation})`);
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
    if (!container) return;

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
                    <button class="icon-btn" onclick="openRenameCalendarModal(${calendar.id}, '${escapeJsAttr(calendar.name)}')"
                            title="${window.MyTeamLocalization.rename}">✏️</button>
                    <button class="icon-btn" onclick="openDeleteCalendarModal(${calendar.id}, '${escapeJsAttr(calendar.name)}')"
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
        const calNameEl = document.getElementById('calendarName');
        if (calNameEl) calNameEl.textContent = calendar.name;
    }
    closeModal('modalSwitchCalendar');
    loadWeekView();
}

function openNewCalendarModal() {
    calendarFormMode = 'create';
    renameCalendarId = null;
    const formTitle = document.getElementById('calendarFormTitle');
    if (formTitle) formTitle.textContent = window.MyTeamLocalization.newCalendar;
    const inputName = document.getElementById('inputCalendarName');
    if (inputName) {
        inputName.value = '';
        openModal('modalCalendarForm');
        inputName.focus();
    } else {
        openModal('modalCalendarForm');
    }
}

function openRenameCalendarModal(calendarId, currentName) {
    calendarFormMode = 'rename';
    renameCalendarId = calendarId;
    const formTitle = document.getElementById('calendarFormTitle');
    if (formTitle) formTitle.textContent = window.MyTeamLocalization.renameCalendarTitle;
    const inputName = document.getElementById('inputCalendarName');
    if (inputName) inputName.value = currentName;
    openModal('modalCalendarForm');
    closeModal('modalSwitchCalendar');
    if (inputName) inputName.focus();
}

function openDeleteCalendarModal(calendarId, calendarName) {
    deleteCalendarId = calendarId;
    const deleteNameEl = document.getElementById('deleteCalendarName');
    if (deleteNameEl) deleteNameEl.textContent = calendarName;
    openModal('modalDeleteConfirm');
    closeModal('modalSwitchCalendar');
}

async function saveCalendar() {
    const inputEl = document.getElementById('inputCalendarName');
    const name = inputEl ? inputEl.value.trim() : '';

    if (!name) {
        window.FeedbackModal.show('warning', window.MyTeamLocalization.pleaseEnterCalendarName);
        return;
    }

    if (name.length > 60) {
        window.FeedbackModal.show('warning', window.MyTeamLocalization.calendarNameMaxLength);
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
            const newCalNameEl = document.getElementById('calendarName');
            if (newCalNameEl) newCalNameEl.textContent = newCalendar.name;
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
                    const renameCalNameEl = document.getElementById('calendarName');
                    if (renameCalNameEl) renameCalNameEl.textContent = name;
                }
            }

            closeModal('modalCalendarForm');
            window.FeedbackModal.show('success', window.MyTeamLocalization.calendarRenamedSuccessfully);
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
                const delCalNameEl = document.getElementById('calendarName');
                if (delCalNameEl) delCalNameEl.textContent = window.MyTeamLocalization.noCalendars;
                const delGridEl = document.getElementById('weekGridContainer');
                if (delGridEl) delGridEl.innerHTML = `<div class="empty-state"><h3>${window.MyTeamLocalization.noCalendars}</h3></div>`;
            }
        }

        closeModal('modalDeleteConfirm');
        window.FeedbackModal.show('success', window.MyTeamLocalization.calendarDeletedSuccessfully);
    } catch (error) {
        // Error already shown by apiCall
    }
}

// ===== MEMBER MANAGEMENT =====

async function openConfigureMembersModal() {
    if (!currentCalendarId) {
        window.FeedbackModal.show('warning', window.MyTeamLocalization.pleaseSelectCalendar);
        return;
    }

    openModal('modalConfigureMembers');
    const container = document.getElementById('memberSelectorContainer');
    if (!container) return;
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
    if (!container) return;

    const html = `
        <div class="member-selector">
            <div class="member-pane">
                <div class="pane-header">Current Members (${currentMembers.length})</div>
                <input type="text" class="pane-search" placeholder="${window.AppLocalizer?.MyTeam_SearchCurrentMembers || 'Search current members...'}"
                       oninput="filterMembers('current', this.value)">
                <div class="member-list" id="currentMembersList">
                    ${renderMemberList(currentMembers, true)}
                </div>
            </div>
            <div class="member-pane">
                <div class="pane-header">Available Users (${availableUsers.length})</div>
                <input type="text" class="pane-search" placeholder="${window.AppLocalizer?.MyTeam_SearchAvailableUsers || 'Search available users...'}"
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
    if (!list) return;
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
        window.FeedbackModal.show('success', window.MyTeamLocalization.membersUpdatedSuccessfully);
    } catch (error) {
        // Error already shown by apiCall
    }
}

// ===== MODAL HELPERS =====

function openModal(modalId) {
    document.getElementById(modalId)?.classList.add('active');
}

function closeModal(modalId) {
    document.getElementById(modalId)?.classList.remove('active');
}

// ===== UTILITY =====
// escapeHtml is provided globally by site.js

/**
 * Escape a string for safe embedding inside a JavaScript string literal
 * within an HTML inline event handler (onclick="fn('${escapeJsAttr(val)}')").
 * Escapes backslashes, single quotes, and HTML-significant characters.
 */
function escapeJsAttr(text) {
    if (!text) return '';
    return String(text)
        .replace(/\\/g, '\\\\')
        .replace(/'/g, "\\'")
        .replace(/</g, '\\x3c')
        .replace(/>/g, '\\x3e')
        .replace(/&/g, '\\x26')
        .replace(/"/g, '\\x22');
}

// ===== OPTIMISTIC UI ENHANCEMENTS =====

/**
 * Add optimistic loading state to an element
 */
function setOptimisticLoading(element, isLoading) {
    if (isLoading) {
        element.classList.add('optimistic-loading');
    } else {
        element.classList.remove('optimistic-loading');
    }
}

/**
 * Flash success animation on an element
 */
function flashSuccess(element) {
    element.classList.add('optimistic-success');
    setTimeout(() => {
        element.classList.remove('optimistic-success');
    }, 600);
}

/**
 * Flash error animation on an element
 */
function flashError(element) {
    element.classList.add('optimistic-error');
    setTimeout(() => {
        element.classList.remove('optimistic-error');
    }, 500);
}

/**
 * Set button loading state
 */
function setButtonLoading(button, isLoading) {
    if (isLoading) {
        button.classList.add('loading');
        button.disabled = true;
    } else {
        button.classList.remove('loading');
        button.disabled = false;
    }
}

/**
 * Enhanced API call with optimistic UI support
 */
async function apiCallWithOptimistic(url, options = {}, targetElement = null) {
    if (targetElement) {
        setOptimisticLoading(targetElement, true);
    }

    try {
        const result = await apiCall(url, options);

        if (targetElement) {
            setOptimisticLoading(targetElement, false);
            flashSuccess(targetElement);
        }

        return result;
    } catch (error) {
        if (targetElement) {
            setOptimisticLoading(targetElement, false);
            flashError(targetElement);
        }
        throw error;
    }
}

/**
 * Optimistically update calendar list UI before API response
 */
function optimisticSwitchCalendar(calendarId) {
    const calendar = calendars.find(c => c.id === calendarId);
    if (calendar) {
        // Immediately update UI
        const optCalNameEl = document.getElementById('calendarName');
        if (optCalNameEl) optCalNameEl.textContent = calendar.name;

        // Update active state in calendar list
        document.querySelectorAll('.calendar-card').forEach(card => {
            card.classList.remove('active');
        });
        const activeCard = document.querySelector(`.calendar-card[onclick*="${calendarId}"]`);
        if (activeCard) {
            activeCard.classList.add('active');
        }
    }
}

/**
 * Enhanced switch to calendar with optimistic UI
 */
async function switchToCalendarOptimistic(calendarId) {
    // Optimistic update
    optimisticSwitchCalendar(calendarId);

    // Update state
    currentCalendarId = calendarId;

    // Close modal
    closeModal('modalSwitchCalendar');

    // Load actual data (will override optimistic state)
    await loadWeekView();
}

// Override the original switchToCalendar with optimistic version
window.switchToCalendar = function(calendarId) {
    switchToCalendarOptimistic(calendarId);
};
