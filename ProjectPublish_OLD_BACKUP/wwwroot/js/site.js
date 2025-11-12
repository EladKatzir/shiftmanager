// Dark mode toggle
document.addEventListener('DOMContentLoaded', function() {
  const root = document.documentElement;
  const saved = localStorage.getItem('theme');

  // Apply saved theme
  if (saved) {
    root.setAttribute('data-theme', saved);
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
      console.log('Theme switched to:', newTheme);
    });
    console.log('Dark mode toggle initialized');
  } else {
    console.warn('Theme toggle button not found');
  }
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
    const data = await res.json();

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
      <label class="form-label">Company</label>
      <select id="companySelect" class="form-input" onchange="filterShiftTypesByCompany()">
        ${companies.map(c => `<option value="${c.id}">${c.name}</option>`).join('')}
      </select>
    </div>
  ` : '';

  const modalHTML = `
    <div id="shiftModal" class="shift-creation-modal">
      <div class="modal-content">
        <div class="modal-header">
          <h2 class="modal-title">Create New Shift</h2>
          <button class="modal-close" onclick="closeShiftModal()">×</button>
        </div>

        <div class="form-group">
          <label class="form-label">Date</label>
          <div id="modalDate" style="padding: .75rem 1rem; background: var(--surface); border-radius: .75rem; color: var(--text); font-weight: 500;"></div>
        </div>

        ${companySelectorHTML}

        <div class="form-group">
          <label class="form-label">Shift Name (Optional)</label>
          <input type="text" id="shiftName" class="form-input" placeholder="e.g., Store Opening, Security Round, etc.">
        </div>

        <div class="form-group">
          <label class="form-label">Shift Type</label>
          <div id="shiftTypeGrid" class="shift-type-grid"></div>
        </div>

        <div class="form-group">
          <label class="form-label">Required Staff</label>
          <div class="staffing-controls">
            <span class="staffing-label">Number of people needed</span>
            <div class="staffing-buttons">
              <button type="button" class="staffing-btn" onclick="adjustStaffingCount(-1)">−</button>
              <div id="staffingCount" class="staffing-count">1</div>
              <button type="button" class="staffing-btn" onclick="adjustStaffingCount(1)">+</button>
            </div>
          </div>
        </div>

        <div class="modal-actions">
          <button type="button" class="btn-modal secondary" onclick="closeShiftModal()">Cancel</button>
          <button type="button" class="btn-modal primary" onclick="createShift()">Create Shift</button>
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
    grid.innerHTML = '<div style="color: var(--muted); text-align: center; padding: 2rem;">No shift types available for this company.</div>';
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
    showToast('Please select a shift type', 'error');
    return;
  }

  if (!currentModalData) {
    showToast('Invalid date data', 'error');
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

    if (!res.ok) {
      const errorData = await res.json();
      throw new Error(errorData.message || 'Failed to create shift');
    }

    const data = await res.json();

    // If shift name is provided, update it through assignment
    if (shiftName) {
      // Store the shift name to be used when first user is assigned
      localStorage.setItem(`pendingShiftName_${currentModalData.date}_${selectedType.dataset.typeId}`, shiftName);
    }

    showToast(`Shift created successfully! ${staffingCount} ${staffingCount === 1 ? 'person' : 'people'} required.`, 'success');
    closeShiftModal();

    // Reload the page to show the new shift
    window.location.reload();

  } catch (error) {
    console.error('Error creating shift:', error);
    showToast(error.message || 'Failed to create shift', 'error');
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
    <h3 style="color: var(--text); margin: 0 0 1rem 0; font-size: 1.5rem;">Access Denied</h3>
    <p style="color: var(--muted); margin: 0 0 2rem 0; line-height: 1.5;">
      You don't have permission to access this page. This area is restricted to managers and administrators.
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
    ">Understood</button>
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
