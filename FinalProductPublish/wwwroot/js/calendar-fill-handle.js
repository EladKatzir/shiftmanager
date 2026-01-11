/**
 * Calendar Fill Handle - Excel-Style Bulk Copy
 * Allows copying shift assignments across multiple days with drag-to-fill functionality
 */

(function() {
    'use strict';

    // State
    let isDraggingHandle = false;
    let sourceCell = null;
    let targetCells = [];
    let fillHandleElement = null;

    /**
     * Initialize Fill Handle functionality
     */
    function initFillHandle() {
        console.log('[Fill Handle] Initializing...');

        // Attach fill handles to cells with assignments
        attachFillHandles();

        // Re-attach on dynamic content updates
        observeCellUpdates();

        console.log('[Fill Handle] Initialized');
    }

    /**
     * Attach fill handles to all eligible cells
     */
    function attachFillHandles() {
        const cells = document.querySelectorAll('.assignment-cell');

        cells.forEach(cell => {
            // Only add fill handle to cells that have at least one assignment
            const hasAssignments = cell.querySelector('.assignment-slot:not(.unassigned)');

            if (hasAssignments && !cell.querySelector('.fill-handle')) {
                addFillHandle(cell);
            }
        });
    }

    /**
     * Add a fill handle to a cell
     */
    function addFillHandle(cell) {
        const existingHandle = cell.querySelector('.fill-handle');
        if (existingHandle) return;

        const handle = document.createElement('div');
        handle.className = 'fill-handle';
        handle.title = 'Drag to copy across days';
        handle.draggable = true;

        // Attach drag events
        handle.addEventListener('mousedown', (e) => handleMouseDown(e, cell));
        handle.addEventListener('dragstart', (e) => handleDragStart(e, cell));
        handle.addEventListener('drag', handleDrag);
        handle.addEventListener('dragend', handleDragEnd);

        cell.appendChild(handle);
    }

    /**
     * Handle mouse down on fill handle
     */
    function handleMouseDown(event, cell) {
        event.stopPropagation();
        fillHandleElement = event.target;
        sourceCell = cell;
    }

    /**
     * Handle drag start
     */
    function handleDragStart(event, cell) {
        isDraggingHandle = true;
        sourceCell = cell;
        targetCells = [];

        // Set drag data
        event.dataTransfer.effectAllowed = 'copy';
        event.dataTransfer.setData('text/plain', 'fill-handle');

        // Highlight source cell
        sourceCell.classList.add('fill-source');

        // Highlight potential target cells
        highlightFillTargets(true);

        console.log('[Fill Handle] Drag started from', cell.dataset.date);
    }

    /**
     * Handle drag (continuous)
     */
    function handleDrag(event) {
        if (!isDraggingHandle) return;

        // Update visual feedback based on mouse position
        const elementUnderMouse = document.elementFromPoint(event.clientX, event.clientY);

        if (elementUnderMouse) {
            const targetCell = elementUnderMouse.closest('.assignment-cell');

            if (targetCell && targetCell !== sourceCell) {
                // Add to target cells if not already present
                if (!targetCells.includes(targetCell)) {
                    targetCells.push(targetCell);
                    targetCell.classList.add('fill-target');
                }
            }
        }
    }

    /**
     * Handle drag end
     */
    async function handleDragEnd(event) {
        if (!isDraggingHandle) return;

        isDraggingHandle = false;

        // Remove visual highlights
        if (sourceCell) {
            sourceCell.classList.remove('fill-source');
        }

        targetCells.forEach(cell => {
            cell.classList.remove('fill-target');
        });

        highlightFillTargets(false);

        // If we have target cells, show the fill options modal
        if (targetCells.length > 0) {
            await showFillOptionsModal();
        } else {
            console.log('[Fill Handle] No target cells selected');
        }

        // Reset state
        sourceCell = null;
        targetCells = [];
    }

    /**
     * Highlight potential fill target cells
     */
    function highlightFillTargets(highlight) {
        if (!sourceCell) return;

        const sourceRow = sourceCell.closest('tr');
        if (!sourceRow) return;

        // Get all cells in the same row (same shift type)
        const cellsInRow = sourceRow.querySelectorAll('.assignment-cell');

        cellsInRow.forEach(cell => {
            if (cell !== sourceCell) {
                if (highlight) {
                    cell.classList.add('fill-available');

                    // Add drop zone behavior
                    cell.addEventListener('dragover', handleDragOver);
                    cell.addEventListener('drop', handleDrop);
                } else {
                    cell.classList.remove('fill-available', 'fill-target');

                    // Remove drop zone behavior
                    cell.removeEventListener('dragover', handleDragOver);
                    cell.removeEventListener('drop', handleDrop);
                }
            }
        });
    }

    /**
     * Handle drag over cell
     */
    function handleDragOver(event) {
        if (!isDraggingHandle) return;

        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';

        const cell = event.currentTarget;

        if (cell && cell !== sourceCell && !targetCells.includes(cell)) {
            targetCells.push(cell);
            cell.classList.add('fill-target');
        }
    }

    /**
     * Handle drop on cell
     */
    function handleDrop(event) {
        event.preventDefault();
        // Actual fill operation happens in dragend
    }

    /**
     * Show fill options modal
     */
    async function showFillOptionsModal() {
        if (!sourceCell || targetCells.length === 0) return;

        const sourceDate = sourceCell.dataset.date;
        const targetDates = targetCells.map(c => c.dataset.date).join(', ');

        const modal = createFillModal(sourceDate, targetDates, targetCells.length);
        document.body.appendChild(modal);

        // Wait for user choice
        const choice = await waitForModalChoice(modal);

        if (choice) {
            await performFillOperation(choice);
        }

        // Clean up modal
        modal.remove();
    }

    /**
     * Create the fill options modal
     */
    function createFillModal(sourceDate, targetDates, targetCount) {
        const modal = document.createElement('div');
        modal.className = 'fill-modal-overlay';
        modal.id = 'fillOptionsModal';

        modal.innerHTML = `
            <div class="fill-modal">
                <div class="fill-modal-header">
                    <h3>Fill Options</h3>
                    <button class="close-modal" data-action="cancel">&times;</button>
                </div>
                <div class="fill-modal-body">
                    <p>Copy assignments from <strong>${formatDate(sourceDate)}</strong> to <strong>${targetCount}</strong> target day${targetCount !== 1 ? 's' : ''}?</p>

                    <div class="fill-options">
                        <label class="fill-option">
                            <input type="radio" name="fillMode" value="exact" checked>
                            <div class="option-content">
                                <strong>Copy Exact</strong>
                                <p>Copy all assignments including trainees and names</p>
                            </div>
                        </label>

                        <label class="fill-option">
                            <input type="radio" name="fillMode" value="staffing">
                            <div class="option-content">
                                <strong>Copy Staffing Only</strong>
                                <p>Copy staffing count and create empty slots</p>
                            </div>
                        </label>

                        <label class="fill-option">
                            <input type="radio" name="fillMode" value="program">
                            <div class="option-content">
                                <strong>Apply Program Defaults</strong>
                                <p>Reset to original Program template settings</p>
                            </div>
                        </label>
                    </div>
                </div>
                <div class="fill-modal-footer">
                    <button class="btn btn-secondary" data-action="cancel">Cancel</button>
                    <button class="btn btn-primary" data-action="confirm">Apply</button>
                </div>
            </div>
        `;

        return modal;
    }

    /**
     * Wait for user to choose an option in the modal
     */
    function waitForModalChoice(modal) {
        return new Promise((resolve) => {
            const confirmBtn = modal.querySelector('[data-action="confirm"]');
            const cancelBtn = modal.querySelector('[data-action="cancel"]');
            const closeBtn = modal.querySelector('.close-modal');

            const handleConfirm = () => {
                const selectedMode = modal.querySelector('input[name="fillMode"]:checked')?.value;
                cleanup();
                resolve(selectedMode);
            };

            const handleCancel = () => {
                cleanup();
                resolve(null);
            };

            const cleanup = () => {
                confirmBtn.removeEventListener('click', handleConfirm);
                cancelBtn.removeEventListener('click', handleCancel);
                closeBtn.removeEventListener('click', handleCancel);
            };

            confirmBtn.addEventListener('click', handleConfirm);
            cancelBtn.addEventListener('click', handleCancel);
            closeBtn.addEventListener('click', handleCancel);

            // Close on overlay click
            modal.addEventListener('click', (e) => {
                if (e.target === modal) {
                    handleCancel();
                }
            });
        });
    }

    /**
     * Perform the fill operation
     */
    async function performFillOperation(mode) {
        if (!sourceCell || targetCells.length === 0) return;

        const sourceInstanceId = parseInt(sourceCell.dataset.instanceId, 10);
        const targetDates = targetCells.map(cell => cell.dataset.date);

        if (!sourceInstanceId) {
            showToast('Invalid source shift', 'error');
            return;
        }

        console.log(`[Fill Handle] Performing ${mode} fill from ${sourceCell.dataset.date} to ${targetDates.length} targets`);

        // Show loading indicator
        const loadingToast = showToast('Applying fill operation...', 'info');

        try {
            const response = await fetch('/Calendar/Table?handler=FillRange', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value
                },
                credentials: 'same-origin',
                body: JSON.stringify({
                    sourceInstanceId,
                    targetDates,
                    mode
                })
            });

            if (!response.ok) {
                const errorData = await response.json();
                throw new Error(errorData.error || 'Fill operation failed');
            }

            const result = await response.json();

            if (result.success) {
                showToast(`Successfully filled ${result.createdCount || targetDates.length} shifts`, 'success');

                // Reload the page to show updated assignments
                setTimeout(() => {
                    window.location.reload();
                }, 1000);
            } else {
                throw new Error(result.error || 'Fill operation failed');
            }

        } catch (error) {
            console.error('[Fill Handle] Fill operation error:', error);
            showToast(error.message || 'Failed to fill shifts', 'error');
        }
    }

    /**
     * Format date for display
     */
    function formatDate(dateString) {
        try {
            const date = new Date(dateString);
            return date.toLocaleDateString('en-US', {
                weekday: 'short',
                month: 'short',
                day: 'numeric'
            });
        } catch {
            return dateString;
        }
    }

    /**
     * Show a toast notification
     */
    function showToast(message, type = 'info') {
        if (window.showToast) {
            return window.showToast(message, type);
        }

        console.log(`[Toast ${type}]`, message);
        alert(message);
        return null;
    }

    /**
     * Observe cell updates to re-attach handles
     */
    function observeCellUpdates() {
        const table = document.querySelector('.shift-table');
        if (!table) return;

        const observer = new MutationObserver((mutations) => {
            let shouldReattach = false;

            mutations.forEach(mutation => {
                if (mutation.type === 'childList' || mutation.type === 'attributes') {
                    shouldReattach = true;
                }
            });

            if (shouldReattach) {
                // Debounce reattachment
                clearTimeout(observerDebounceTimer);
                observerDebounceTimer = setTimeout(() => {
                    attachFillHandles();
                }, 500);
            }
        });

        observer.observe(table, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class']
        });
    }

    let observerDebounceTimer = null;

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initFillHandle);
    } else {
        initFillHandle();
    }

})();
