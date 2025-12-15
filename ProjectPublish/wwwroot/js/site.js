// Dark mode toggle with system preference support
document.addEventListener('DOMContentLoaded', function() {
  const root = document.documentElement;
  const saved = localStorage.getItem('theme');

  // Apply saved theme or detect system preference
  if (saved) {
    root.setAttribute('data-theme', saved);
    console.log('Using saved theme:', saved);
  } else {
    // Detect system preference
    const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    const systemTheme = prefersDark ? 'dark' : 'light';
    root.setAttribute('data-theme', systemTheme);
    console.log('No saved theme, using system preference:', systemTheme);
  }

  // Listen for system theme changes
  if (window.matchMedia) {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function(e) {
      // Only auto-switch if user hasn't manually set a preference
      if (!localStorage.getItem('theme')) {
        const newTheme = e.matches ? 'dark' : 'light';
        root.setAttribute('data-theme', newTheme);
        console.log('System theme changed to:', newTheme);
      }
    });
  }

  // Set up toggle button
  const btn = document.getElementById('themeToggle');
  if (btn) {
    btn.addEventListener('click', function(e) {
      e.preventDefault();
      const current = root.getAttribute('data-theme');
      const newTheme = current === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-theme', newTheme);
      localStorage.setItem('theme', newTheme);
      console.log('Theme manually switched to:', newTheme);
    });
    console.log('Dark mode toggle initialized');
  } else {
    console.warn('Theme toggle button not found');
  }

  // Easter egg: Shift Swap game (Ctrl+Click on .brand)
  // Using event delegation to catch clicks anywhere within .brand
  document.addEventListener('click', async function(e) {
    // Check if the click (or any parent of the clicked element) is within .brand
    const brandElement = e.target.closest('.brand');

    // If not clicking within .brand area, ignore
    if (!brandElement) return;

    console.log('Brand area clicked! Ctrl:', e.ctrlKey, 'Meta:', e.metaKey, 'Clicked element:', e.target.tagName, e.target.className);

    // Only trigger game if Ctrl/Cmd key is pressed
    if (e.ctrlKey || e.metaKey) {
      e.preventDefault();
      e.stopPropagation();

      console.log('Ctrl+click detected on .brand! ShiftSwapGame available:', !!window.ShiftSwapGame);

      // Dynamically load game assets if not already loaded
      if (!window.ShiftSwapGame) {
        console.log('Loading Shift Swap game assets...');

        // Load CSS
        if (!document.querySelector('link[href*="shift-swap-game.css"]')) {
          const cssLink = document.createElement('link');
          cssLink.rel = 'stylesheet';
          cssLink.href = '/css/shift-swap-game.css?v=' + Date.now();
          document.head.appendChild(cssLink);
        }

        // Load JavaScript
        const script = document.createElement('script');
        script.src = '/js/shift-swap-game.js?v=' + Date.now();
        document.head.appendChild(script);

        // Wait for script to load
        await new Promise((resolve) => {
          script.onload = resolve;
          script.onerror = () => {
            console.error('Failed to load Shift Swap game script');
            resolve();
          };
        });
      }

      // Open the Shift Swap game
      if (window.ShiftSwapGame) {
        console.log('Opening Shift Swap game...');
        window.ShiftSwapGame.open();
      } else {
        console.error('ShiftSwapGame not loaded!');
      }
    }
    // Normal clicks work as usual (no special handling needed)
  });

  console.log('Easter egg Ctrl+click handler initialized for .brand');

});

// Enhanced staffing adjustments with better UX
async function adjustStaffing(url, payload, onOk, onError) {
  const button = event.target;
  const originalText = button.textContent;

  // Add loading state
  button.disabled = true;
  button.style.opacity = '0.6';
  button.textContent = '⋯';

  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    // Try to parse as JSON, fallback to text if it fails
    let data;
    const contentType = res.headers.get('content-type');
    try {
      data = contentType && contentType.includes('application/json')
        ? await res.json()
        : { message: await res.text() };
    } catch (parseError) {
      console.warn('Failed to parse response, using text:', parseError);
      data = { message: await res.text() };
    }

    if (res.ok) {
      // Success animation
      button.style.background = '#2e7d32';
      button.style.color = 'white';
      setTimeout(() => {
        button.style.background = '';
        button.style.color = '';
      }, 300);

      onOk(data);
    } else {
      // Error animation
      button.style.background = '#dc3545';
      button.style.color = 'white';
      setTimeout(() => {
        button.style.background = '';
        button.style.color = '';
      }, 300);

      // Show toast notification instead of alert
      showToast(data.message || 'Operation failed', 'error');
      onError && onError(data);
    }
  } catch (e) {
    console.error(e);
    button.style.background = '#dc3545';
    button.style.color = 'white';
    setTimeout(() => {
      button.style.background = '';
      button.style.color = '';
    }, 300);
    showToast('Network error - please try again', 'error');
  } finally {
    // Reset button state
    button.disabled = false;
    button.style.opacity = '';
    button.textContent = originalText;
  }
}

// Toast notification system
function showToast(message, type = 'info') {
  const toast = document.createElement('div');
  toast.className = `toast toast-${type}`;
  toast.textContent = message;
  toast.style.cssText = `
    position: fixed;
    top: 20px;
    right: 20px;
    padding: 12px 20px;
    border-radius: 6px;
    color: white;
    font-weight: 500;
    z-index: 1000;
    transform: translateX(400px);
    transition: transform 0.3s ease;
    max-width: 300px;
    word-wrap: break-word;
    ${type === 'error' ? 'background: #dc3545;' : 'background: #2e7d32;'}
  `;

  document.body.appendChild(toast);

  // Slide in
  setTimeout(() => {
    toast.style.transform = 'translateX(0)';
  }, 10);

  // Slide out and remove
  setTimeout(() => {
    toast.style.transform = 'translateX(400px)';
    setTimeout(() => {
      document.body.removeChild(toast);
    }, 300);
  }, 3000);
}

// Add keyboard shortcuts for calendar navigation
document.addEventListener('keydown', (e) => {
  if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;

  if (e.key === 'ArrowLeft' || e.key === 'h') {
    const prevButton = document.querySelector('a[href*="Calendar/Month"]:first-child');
    if (prevButton) prevButton.click();
  } else if (e.key === 'ArrowRight' || e.key === 'l') {
    const nextButton = document.querySelector('a[href*="Calendar/Month"]:nth-child(3)');
    if (nextButton) nextButton.click();
  } else if (e.key === 't' || e.key === 'Home') {
    const todayButton = document.querySelector('a[href="/Calendar/Month"]');
    if (todayButton) todayButton.click();
  }
});

// Advanced tooltip portal system
let tooltipPortal = null;

document.addEventListener('DOMContentLoaded', () => {
  // Create tooltip portal container
  tooltipPortal = document.createElement('div');
  tooltipPortal.id = 'tooltip-portal';
  tooltipPortal.style.cssText = `
    position: fixed;
    top: 0;
    left: 0;
    width: 100vw;
    height: 100vh;
    pointer-events: none;
    z-index: 2147483647;
  `;
  document.body.appendChild(tooltipPortal);

  document.addEventListener('mouseenter', (e) => {
    // Ensure e.target is an Element before calling closest()
    if (!(e.target instanceof Element)) return;

    const tooltip = e.target.closest('.assignment-tooltip');
    if (!tooltip) return;

    const tooltipContent = tooltip.querySelector('.tooltip-content');
    if (!tooltipContent) return;

    // Clone tooltip content and move to portal
    const clonedTooltip = tooltipContent.cloneNode(true);
    clonedTooltip.style.cssText = `
      position: fixed !important;
      z-index: 2147483647 !important;
      background: rgba(0, 0, 0, 0.9) !important;
      color: white !important;
      border: 2px solid white !important;
      padding: 1rem !important;
      border-radius: 8px !important;
      min-width: 280px !important;
      max-width: 450px !important;
      font-size: 14px !important;
      line-height: 1.4 !important;
      display: block !important;
      visibility: visible !important;
      opacity: 1 !important;
      transform: none !important;
      pointer-events: none !important;
    `;

    tooltipPortal.appendChild(clonedTooltip);

    // Position tooltip near mouse cursor
    const handleMouseMove = (moveEvent) => {
      const x = moveEvent.clientX;
      const y = moveEvent.clientY;
      const rect = clonedTooltip.getBoundingClientRect();
      const viewportWidth = window.innerWidth;
      const viewportHeight = window.innerHeight;

      let left = x + 15;
      let top = y + 15;

      // Adjust if tooltip would go off right edge
      if (left + rect.width > viewportWidth - 20) {
        left = x - rect.width - 15;
      }

      // Adjust if tooltip would go off bottom edge
      if (top + rect.height > viewportHeight - 20) {
        top = y - rect.height - 15;
      }

      // Adjust if tooltip would go off left edge
      if (left < 10) {
        left = 10;
      }

      // Adjust if tooltip would go off top edge
      if (top < 10) {
        top = 10;
      }

      clonedTooltip.style.left = left + 'px';
      clonedTooltip.style.top = top + 'px';
    };

    // Initial positioning
    handleMouseMove(e);

    tooltip.addEventListener('mousemove', handleMouseMove);
    tooltip.addEventListener('mouseleave', () => {
      tooltip.removeEventListener('mousemove', handleMouseMove);
      if (clonedTooltip.parentNode) {
        clonedTooltip.parentNode.removeChild(clonedTooltip);
      }
    }, { once: true });
  }, true);
});


// Modern Shift Creation System
let currentModalData = null;

function openShiftModal(date) {
  console.log('Opening shift modal for date:', date);
  const availableTypes = window.shiftTypes || [];
  console.log('Available shift types:', availableTypes);
  currentModalData = { date, availableTypes };

  const modal = document.getElementById('shiftModal');
  if (!modal) {
    createShiftModal();
  }

  // Reset modal state
  resetShiftModal();

  // Populate shift types
  populateShiftTypes(availableTypes);

  // Set date in modal
  document.getElementById('modalDate').textContent = new Date(date).toLocaleDateString('en-US', {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric'
  });

  // Show modal
  document.getElementById('shiftModal').classList.add('show');
  document.body.style.overflow = 'hidden';
}

function createShiftModal() {
  const companies = window.companies || [];
  const showCompanySelector = companies.length > 1;

  const companySelectorHTML = showCompanySelector ? `
    <div class="form-group">
      <label class="form-label">${window.AppLocalizer.Company}</label>
      <select id="companySelect" class="form-input" onchange="filterShiftTypesByCompany()">
        ${companies.map(c => `<option value="${c.id}">${c.name}</option>`).join('')}
      </select>
    </div>
  ` : '';

  const modalHTML = `
    <div id="shiftModal" class="shift-creation-modal">
      <div class="modal-content">
        <div class="modal-header">
          <h2 class="modal-title">${window.AppLocalizer.CreateNewShift}</h2>
          <button class="modal-close" onclick="closeShiftModal()">×</button>
        </div>

        <div class="form-group">
          <label class="form-label">${window.AppLocalizer.Date}</label>
          <div id="modalDate" style="padding: .75rem 1rem; background: var(--surface); border-radius: .75rem; color: var(--text); font-weight: 500;"></div>
        </div>

        ${companySelectorHTML}

        <div class="form-group">
          <label class="form-label">${window.AppLocalizer.ShiftNameOptional}</label>
          <input type="text" id="shiftName" class="form-input" placeholder="${window.AppLocalizer.ShiftNamePlaceholder}">
        </div>

        <div class="form-group">
          <label class="form-label">${window.AppLocalizer.ShiftType}</label>
          <div id="shiftTypeGrid" class="shift-type-grid"></div>
        </div>

        <div class="form-group">
          <label class="form-label">${window.AppLocalizer.RequiredStaff}</label>
          <div class="staffing-controls">
            <span class="staffing-label">${window.AppLocalizer.NumberOfPeopleNeeded}</span>
            <div class="staffing-buttons">
              <button type="button" class="staffing-btn" onclick="adjustStaffingCount(-1)">−</button>
              <div id="staffingCount" class="staffing-count">1</div>
              <button type="button" class="staffing-btn" onclick="adjustStaffingCount(1)">+</button>
            </div>
          </div>
        </div>

        <div class="modal-actions">
          <button type="button" class="btn-modal secondary" onclick="closeShiftModal()">${window.AppLocalizer.Cancel}</button>
          <button type="button" class="btn-modal primary" onclick="createShift()">${window.AppLocalizer.CreateShift}</button>
        </div>
      </div>
    </div>
  `;

  document.body.insertAdjacentHTML('beforeend', modalHTML);

  // Close modal when clicking outside
  document.getElementById('shiftModal').addEventListener('click', (e) => {
    if (e.target.id === 'shiftModal') {
      closeShiftModal();
    }
  });

  // Close modal with Escape key
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && document.getElementById('shiftModal').classList.contains('show')) {
      closeShiftModal();
    }
  });
}

function resetShiftModal() {
  document.getElementById('shiftName').value = '';
  document.getElementById('staffingCount').textContent = '1';
  document.querySelectorAll('.shift-type-option').forEach(option => {
    option.classList.remove('selected');
  });
}

function populateShiftTypes(types) {
  const grid = document.getElementById('shiftTypeGrid');
  grid.innerHTML = '';

  // Get selected company if selector exists
  const companySelect = document.getElementById('companySelect');
  const selectedCompanyId = companySelect ? parseInt(companySelect.value) : null;

  // Filter types by selected company
  const filteredTypes = selectedCompanyId
    ? types.filter(t => t.companyId === selectedCompanyId)
    : types;

  if (filteredTypes.length === 0) {
    grid.innerHTML = `<div style="color: var(--muted); text-align: center; padding: 2rem;">${window.AppLocalizer.NoShiftTypesAvailable}</div>`;
    return;
  }

  filteredTypes.forEach(type => {
    const option = document.createElement('div');
    option.className = 'shift-type-option';
    option.dataset.typeId = type.id;
    option.dataset.typeKey = type.key;
    option.onclick = () => selectShiftType(option);

    const companyLabel = type.companyName && selectedCompanyId === null
      ? `<div class="shift-type-company">${type.companyName}</div>`
      : '';

    option.innerHTML = `
      <div class="shift-type-name">${type.name}</div>
      <div class="shift-type-time">${type.start} - ${type.end}</div>
      ${companyLabel}
    `;

    grid.appendChild(option);
  });
}

function filterShiftTypesByCompany() {
  if (!currentModalData) return;
  populateShiftTypes(currentModalData.availableTypes);
}

function selectShiftType(option) {
  // Remove selection from all options
  document.querySelectorAll('.shift-type-option').forEach(opt => {
    opt.classList.remove('selected');
  });

  // Select clicked option
  option.classList.add('selected');
}

function adjustStaffingCount(delta) {
  const countElement = document.getElementById('staffingCount');
  let current = parseInt(countElement.textContent);
  current = Math.max(1, current + delta);
  countElement.textContent = current;
}

function closeShiftModal() {
  document.getElementById('shiftModal').classList.remove('show');
  document.body.style.overflow = '';
}

async function createShift() {
  const shiftName = document.getElementById('shiftName').value.trim();
  const staffingCount = parseInt(document.getElementById('staffingCount').textContent);
  const selectedType = document.querySelector('.shift-type-option.selected');

  if (!selectedType) {
    showToast(window.AppLocalizer.PleaseSelectShiftType, 'error');
    return;
  }

  if (!currentModalData) {
    showToast(window.AppLocalizer.InvalidDateData, 'error');
    return;
  }

  // Get companyId from selected shift type
  const selectedTypeId = parseInt(selectedType.dataset.typeId);
  const shiftType = currentModalData.availableTypes.find(t => t.id === selectedTypeId);

  const payload = {
    date: currentModalData.date,
    shiftTypeId: selectedTypeId,
    delta: staffingCount,
    concurrency: 0,
    companyId: shiftType ? shiftType.companyId : null
  };

  try {
    // First create the shift instance with required staffing
    const adjustUrl = window.location.pathname.includes('Month') ? '/Calendar/Month?handler=Adjust' :
                     window.location.pathname.includes('Week') ? '/Calendar/Week?handler=Adjust' :
                     '/Calendar/Day?handler=Adjust';

    const res = await fetch(adjustUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    // Try to parse as JSON, fallback to text if it fails
    let data;
    const contentType = res.headers.get('content-type');
    try {
      data = contentType && contentType.includes('application/json')
        ? await res.json()
        : { message: await res.text() };
    } catch (parseError) {
      console.warn('Failed to parse response, using text:', parseError);
      data = { message: await res.text() };
    }

    if (!res.ok) {
      throw new Error(data.message || window.AppLocalizer.FailedToCreateShift);
    }

    // If shift name is provided, update it through assignment
    if (shiftName) {
      // Store the shift name to be used when first user is assigned
      localStorage.setItem(`pendingShiftName_${currentModalData.date}_${selectedType.dataset.typeId}`, shiftName);
    }

    const personText = staffingCount === 1 ? window.AppLocalizer.Person : window.AppLocalizer.People;
    showToast(`${window.AppLocalizer.ShiftCreatedSuccessfully} ${staffingCount} ${personText} ${window.AppLocalizer.Required}.`, 'success');
    closeShiftModal();

    // Reload the page to show the new shift
    window.location.reload();

  } catch (error) {
    console.error('Error creating shift:', error);
    showToast(error.message || window.AppLocalizer.FailedToCreateShift, 'error');
  }
}

// Enhanced toast with success styling
function showToast(message, type = 'info') {
  const toast = document.createElement('div');
  toast.className = `toast toast-${type}`;
  toast.textContent = message;
  toast.style.cssText = `
    position: fixed;
    top: 20px;
    right: 20px;
    padding: 12px 20px;
    border-radius: 8px;
    color: white;
    font-weight: 500;
    z-index: 10001;
    transform: translateX(400px);
    transition: transform 0.3s ease;
    max-width: 300px;
    word-wrap: break-word;
    ${type === 'error' ? 'background: #dc3545;' : type === 'success' ? 'background: #28a745;' : 'background: #2e7d32;'}
  `;

  document.body.appendChild(toast);

  // Slide in
  setTimeout(() => {
    toast.style.transform = 'translateX(0)';
  }, 10);

  // Slide out and remove
  setTimeout(() => {
    toast.style.transform = 'translateX(400px)';
    setTimeout(() => {
      if (document.body.contains(toast)) {
        document.body.removeChild(toast);
      }
    }, 300);
  }, 4000);
}

window.adjustStaffing = adjustStaffing;
window.openShiftModal = openShiftModal;

// Access Denied Popup System
document.addEventListener('DOMContentLoaded', function() {
  // Check for access denied parameter
  const urlParams = new URLSearchParams(window.location.search);
  if (urlParams.get('accessDenied') === 'true') {
    showAccessDeniedPopup();

    // Remove the parameter from URL without refreshing
    const url = new URL(window.location);
    url.searchParams.delete('accessDenied');
    window.history.replaceState({}, document.title, url.toString());
  }
});

function showAccessDeniedPopup() {
  // Create popup overlay
  const overlay = document.createElement('div');
  overlay.style.cssText = `
    position: fixed;
    top: 0;
    left: 0;
    width: 100vw;
    height: 100vh;
    background: rgba(0, 0, 0, 0.5);
    z-index: 10000;
    display: flex;
    align-items: center;
    justify-content: center;
    animation: fadeIn 0.3s ease;
  `;

  // Create popup content
  const popup = document.createElement('div');
  popup.style.cssText = `
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 1rem;
    padding: 2rem;
    max-width: 400px;
    text-align: center;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
    animation: slideIn 0.3s ease;
  `;

  popup.innerHTML = `
    <div style="color: var(--danger); font-size: 3rem; margin-bottom: 1rem;">🚫</div>
    <h3 style="color: var(--text); margin: 0 0 1rem 0; font-size: 1.5rem;">${window.AppLocalizer.AccessDenied}</h3>
    <p style="color: var(--muted); margin: 0 0 2rem 0; line-height: 1.5;">
      ${window.AppLocalizer.AccessDeniedMessage}
    </p>
    <button id="closeAccessDenied" style="
      background: var(--primary);
      color: white;
      border: none;
      padding: 0.75rem 2rem;
      border-radius: 0.5rem;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.3s ease;
    ">${window.AppLocalizer.Understood}</button>
  `;

  overlay.appendChild(popup);
  document.body.appendChild(overlay);

  // Add CSS animations
  const style = document.createElement('style');
  style.textContent = `
    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }
    @keyframes slideIn {
      from { transform: translateY(-20px) scale(0.95); opacity: 0; }
      to { transform: translateY(0) scale(1); opacity: 1; }
    }
  `;
  document.head.appendChild(style);

  // Close popup handlers
  function closePopup() {
    overlay.style.animation = 'fadeIn 0.3s ease reverse';
    popup.style.animation = 'slideIn 0.3s ease reverse';
    setTimeout(() => {
      if (document.body.contains(overlay)) {
        document.body.removeChild(overlay);
      }
      if (document.head.contains(style)) {
        document.head.removeChild(style);
      }
    }, 300);
  }

  document.getElementById('closeAccessDenied').addEventListener('click', closePopup);
  overlay.addEventListener('click', function(e) {
    if (e.target === overlay) {
      closePopup();
    }
  });

  // Close with Escape key
  function handleEscape(e) {
    if (e.key === 'Escape') {
      closePopup();
      document.removeEventListener('keydown', handleEscape);
    }
  }
  document.addEventListener('keydown', handleEscape);

  // Auto-close after 5 seconds
  setTimeout(closePopup, 5000);
}

// ============= SHIFT INSTANCE DELETION =============

/**
 * Delete a shift instance and all its assignments
 * @param {string} pageUrl - The page URL (e.g., '/Calendar/Month', '/Calendar/Week', '/Calendar/Day')
 * @param {number} instanceId - The shift instance ID to delete
 * @param {Event} event - The click event
 */
async function confirmDeleteShiftInstance(pageUrl, instanceId, event) {
    event.stopPropagation();

    if (!confirm(window.AppLocalizer.ConfirmDeleteShift)) {
        return;
    }

    try {
        const response = await fetch(`${pageUrl}?handler=DeleteShiftInstance`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                shiftInstanceId: instanceId
            })
        });

        const result = await response.json();

        if (result.success) {
            // Reload page to show updated state
            location.reload();
        } else {
            alert('Error: ' + result.error);
        }
    } catch (error) {
        console.error('Error deleting shift instance:', error);
        alert('Failed to delete shift. Please try again.');
    }
}

// ============= COMMAND PALETTE =============

// Note: Command palette pages will use localized strings from window.AppLocalizer
function getCommandPalettePages() {
  return [
    // Manager/Admin Pages
    { title: window.AppLocalizer.Home, subtitle: window.AppLocalizer.DashboardOverview, url: '/Home/Index', icon: '🏠', roles: ['Manager', 'Director', 'Owner'] },
    { title: `${window.AppLocalizer.Calendar} - Month View`, subtitle: window.AppLocalizer.MonthlySchedule, url: '/Calendar/Month', icon: '📅', roles: ['all'] },
    { title: `${window.AppLocalizer.Calendar} - Week View`, subtitle: window.AppLocalizer.WeeklySchedule, url: '/Calendar/Week', icon: '📆', roles: ['all'] },
    { title: `${window.AppLocalizer.Calendar} - Day View`, subtitle: window.AppLocalizer.DailySchedule, url: '/Calendar/Day', icon: '📋', roles: ['all'] },
    { title: 'Requests', subtitle: window.AppLocalizer.ManageRequests, url: '/Requests/Index', icon: '📝', roles: ['Manager', 'Director', 'Owner'] },
    { title: 'Analytics', subtitle: window.AppLocalizer.ViewReports, url: '/Admin/Analytics', icon: '📊', roles: ['Manager', 'Director', 'Owner'] },
    { title: 'Users', subtitle: window.AppLocalizer.ManageEmployees, url: '/Admin/Users', icon: '👥', roles: ['Manager', 'Director', 'Owner'] },
    { title: 'Companies', subtitle: window.AppLocalizer.ManageOrganizations, url: '/Admin/Companies', icon: '🏢', roles: ['Owner'] },
    { title: 'Directors', subtitle: window.AppLocalizer.AssignDirectors, url: '/Admin/Directors', icon: '👔', roles: ['Owner'] },
    { title: 'Configuration', subtitle: window.AppLocalizer.SystemSettings, url: '/Admin/Config', icon: '⚙️', roles: ['Manager', 'Director', 'Owner'] },
    { title: 'Shift Types', subtitle: window.AppLocalizer.ManageShiftDefinitions, url: '/Admin/ShiftTypes', icon: '🕐', roles: ['Manager', 'Director', 'Owner'] },
    { title: 'Time Off', subtitle: window.AppLocalizer.ApprovedTimeOff, url: '/Admin/TimeOff', icon: '🏖️', roles: ['Manager', 'Director', 'Owner'] },
    { title: 'Audit Log', subtitle: window.AppLocalizer.SystemActivityLog, url: '/Admin/AuditLog', icon: '📜', roles: ['Owner'] },
    { title: 'Chores', subtitle: window.AppLocalizer.TaskManagement, url: '/Public/Chores', icon: '🗂️', roles: ['all'] },
    { title: 'On Duty', subtitle: window.AppLocalizer.CurrentDutyRoster, url: '/Public/OnDuty', icon: '🎯', roles: ['all'] },

    // Employee Pages
    { title: 'My Requests', subtitle: window.AppLocalizer.ViewMyRequests, url: '/My/Requests', icon: '📝', roles: ['Employee', 'Trainee'] },
    { title: 'My Team', subtitle: window.AppLocalizer.ViewTeamMembers, url: '/MyTeam/Index', icon: '👥', roles: ['Employee', 'Trainee'] },
    { title: 'My Profile', subtitle: window.AppLocalizer.UpdateMyInformation, url: '/My/Profile', icon: '👤', roles: ['Employee', 'Trainee'] },
    { title: 'Notifications', subtitle: window.AppLocalizer.ViewNotifications, url: '/My/NotificationCenter', icon: '🔔', roles: ['all'] }
  ];
}

let commandPaletteState = {
  isOpen: false,
  selectedIndex: -1,
  filteredPages: [],
  recentPages: []
};

// Initialize command palette
document.addEventListener('DOMContentLoaded', function() {
  // Load recent pages from localStorage
  const stored = localStorage.getItem('commandPaletteRecent');
  if (stored) {
    try {
      commandPaletteState.recentPages = JSON.parse(stored);
    } catch (e) {
      console.warn('Failed to parse recent pages:', e);
      commandPaletteState.recentPages = [];
    }
  }

  // Keyboard shortcut: Ctrl/Cmd + K
  document.addEventListener('keydown', function(e) {
    // Ctrl/Cmd + K
    if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
      e.preventDefault();
      toggleCommandPalette();
    }

    // Escape to close
    if (e.key === 'Escape' && commandPaletteState.isOpen) {
      closeCommandPalette();
    }

    // Arrow navigation when palette is open
    if (commandPaletteState.isOpen) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        navigateCommandPalette(1);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        navigateCommandPalette(-1);
      } else if (e.key === 'Enter') {
        e.preventDefault();
        selectCommandPaletteItem();
      }
    }
  });

  // Search input handler
  const searchInput = document.getElementById('commandPaletteInput');
  if (searchInput) {
    searchInput.addEventListener('input', function(e) {
      filterCommandPalette(e.target.value);
    });
  }
});

function toggleCommandPalette() {
  if (commandPaletteState.isOpen) {
    closeCommandPalette();
  } else {
    openCommandPalette();
  }
}

function openCommandPalette() {
  const palette = document.getElementById('commandPalette');
  if (!palette) return;

  commandPaletteState.isOpen = true;
  palette.style.display = 'flex';

  // Focus search input
  const searchInput = document.getElementById('commandPaletteInput');
  if (searchInput) {
    searchInput.value = '';
    searchInput.focus();
  }

  // Show recent pages initially
  filterCommandPalette('');

  // Prevent body scroll
  document.body.style.overflow = 'hidden';
}

function closeCommandPalette() {
  const palette = document.getElementById('commandPalette');
  if (!palette) return;

  commandPaletteState.isOpen = false;
  commandPaletteState.selectedIndex = -1;
  palette.style.display = 'none';

  // Restore body scroll
  document.body.style.overflow = '';
}

function filterCommandPalette(query) {
  const resultsContainer = document.getElementById('commandPaletteResults');
  if (!resultsContainer) return;

  // Get user role from page (if available)
  const userRole = getUserRole();

  // Get localized command palette pages
  const commandPalettePages = getCommandPalettePages();

  // Filter pages based on query and role
  const lowerQuery = query.toLowerCase().trim();

  if (lowerQuery === '') {
    // Show recent pages
    commandPaletteState.filteredPages = commandPaletteState.recentPages
      .map(url => commandPalettePages.find(p => p.url === url))
      .filter(p => p && canAccessPage(p, userRole))
      .slice(0, 5);
  } else {
    // Search all pages
    commandPaletteState.filteredPages = commandPalettePages
      .filter(page => {
        if (!canAccessPage(page, userRole)) return false;
        const titleMatch = page.title.toLowerCase().includes(lowerQuery);
        const subtitleMatch = page.subtitle.toLowerCase().includes(lowerQuery);
        return titleMatch || subtitleMatch;
      });
  }

  // Reset selection
  commandPaletteState.selectedIndex = commandPaletteState.filteredPages.length > 0 ? 0 : -1;

  // Render results
  renderCommandPaletteResults(lowerQuery === '');
}

function renderCommandPaletteResults(showingRecent) {
  const resultsContainer = document.getElementById('commandPaletteResults');
  if (!resultsContainer) return;

  resultsContainer.innerHTML = '';

  if (commandPaletteState.filteredPages.length === 0) {
    // Show empty state
    resultsContainer.innerHTML = `
      <div class="command-palette-empty">
        <div class="command-palette-empty-icon">🔍</div>
        <div class="command-palette-empty-text">${window.AppLocalizer.NoResultsFound}</div>
      </div>
    `;
    return;
  }

  // Show section title if showing recent
  if (showingRecent && commandPaletteState.filteredPages.length > 0) {
    const sectionTitle = document.createElement('div');
    sectionTitle.className = 'command-palette-section-title';
    sectionTitle.textContent = window.AppLocalizer.Recent;
    resultsContainer.appendChild(sectionTitle);
  }

  // Render items
  commandPaletteState.filteredPages.forEach((page, index) => {
    const item = document.createElement('a');
    item.className = 'command-palette-item';
    item.href = page.url;
    if (index === commandPaletteState.selectedIndex) {
      item.classList.add('selected');
    }

    item.innerHTML = `
      <div class="command-palette-item-icon">${page.icon}</div>
      <div class="command-palette-item-content">
        <div class="command-palette-item-title">${page.title}</div>
        <div class="command-palette-item-subtitle">${page.subtitle}</div>
      </div>
    `;

    item.addEventListener('click', function(e) {
      e.preventDefault();
      navigateToPage(page.url);
    });

    item.addEventListener('mouseenter', function() {
      commandPaletteState.selectedIndex = index;
      updateSelectedItem();
    });

    resultsContainer.appendChild(item);
  });
}

function navigateCommandPalette(direction) {
  if (commandPaletteState.filteredPages.length === 0) return;

  commandPaletteState.selectedIndex += direction;

  // Wrap around
  if (commandPaletteState.selectedIndex < 0) {
    commandPaletteState.selectedIndex = commandPaletteState.filteredPages.length - 1;
  } else if (commandPaletteState.selectedIndex >= commandPaletteState.filteredPages.length) {
    commandPaletteState.selectedIndex = 0;
  }

  updateSelectedItem();
}

function updateSelectedItem() {
  const items = document.querySelectorAll('.command-palette-item');
  items.forEach((item, index) => {
    if (index === commandPaletteState.selectedIndex) {
      item.classList.add('selected');
      item.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    } else {
      item.classList.remove('selected');
    }
  });
}

function selectCommandPaletteItem() {
  if (commandPaletteState.selectedIndex < 0 ||
      commandPaletteState.selectedIndex >= commandPaletteState.filteredPages.length) {
    return;
  }

  const selectedPage = commandPaletteState.filteredPages[commandPaletteState.selectedIndex];
  navigateToPage(selectedPage.url);
}

function navigateToPage(url) {
  // Add to recent pages
  addToRecentPages(url);

  // Close palette
  closeCommandPalette();

  // Navigate
  window.location.href = url;
}

function addToRecentPages(url) {
  // Remove if already exists
  commandPaletteState.recentPages = commandPaletteState.recentPages.filter(u => u !== url);

  // Add to front
  commandPaletteState.recentPages.unshift(url);

  // Keep only last 10
  commandPaletteState.recentPages = commandPaletteState.recentPages.slice(0, 10);

  // Save to localStorage
  try {
    localStorage.setItem('commandPaletteRecent', JSON.stringify(commandPaletteState.recentPages));
  } catch (e) {
    console.warn('Failed to save recent pages:', e);
  }
}

function canAccessPage(page, userRole) {
  if (!page.roles || page.roles.includes('all')) return true;
  if (!userRole) return true; // Show all if role is unknown
  return page.roles.includes(userRole);
}

function getUserRole() {
  // Try to determine user role from page context
  // This is a simplified implementation - adjust based on your needs
  const body = document.body;
  const sidebar = document.querySelector('.app-sidebar-nav');

  if (!sidebar) return null;

  // Check for Owner-specific links
  if (sidebar.querySelector('a[href*="/Admin/Companies"]')) {
    return 'Owner';
  }

  // Check for Manager/Director links
  if (sidebar.querySelector('a[href*="/Admin/Users"]')) {
    return 'Manager';
  }

  // Check for Employee links
  if (sidebar.querySelector('a[href*="/My/Profile"]')) {
    return 'Employee';
  }

  return null;
}

// Make function globally accessible
window.closeCommandPalette = closeCommandPalette;

// ============= USER MENU NAVIGATION =============

// Navigate to profile when clicking user menu
document.addEventListener('DOMContentLoaded', function() {
  const userMenu = document.querySelector('.user-menu');
  if (userMenu) {
    userMenu.style.cursor = 'pointer';
    userMenu.addEventListener('click', function(e) {
      e.preventDefault();
      window.location.href = '/My/Profile';
    });
  }
});
