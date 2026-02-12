/**
 * Keyboard Navigation Utility (B-037)
 * Provides comprehensive keyboard navigation support for accessibility.
 *
 * Features:
 * 1. Dropdown/Menu keyboard navigation (Arrow keys, Enter, ESC)
 * 2. Calendar grid navigation (Arrow keys)
 * 3. Global ESC handler for overlays
 * 4. Space toggles checkboxes
 * 5. Enter activates buttons/links
 */
(function() {
    'use strict';

    // ============= DROPDOWN KEYBOARD NAVIGATION =============

    /**
     * Initialize keyboard navigation for a dropdown/combobox
     * @param {Object} config - Configuration object
     * @param {HTMLElement} config.trigger - The button/element that opens the dropdown
     * @param {HTMLElement} config.dropdown - The dropdown container
     * @param {string} config.optionSelector - CSS selector for options within dropdown
     * @param {Function} config.onSelect - Callback when option is selected
     * @param {Function} config.onClose - Callback when dropdown closes
     * @param {boolean} config.closeOnSelect - Whether to close on selection (default: true)
     */
    function initDropdownKeyboardNav(config) {
        var trigger = config.trigger;
        var dropdown = config.dropdown;
        var optionSelector = config.optionSelector || '[role="option"], .dropdown-option';
        var onSelect = config.onSelect || function() {};
        var onClose = config.onClose || function() {};
        var closeOnSelect = config.closeOnSelect !== false;

        var focusedIndex = -1;
        var options = [];

        // TypeAhead support
        var typeAheadBuffer = '';
        var typeAheadTimeout = null;
        var TYPE_AHEAD_DELAY = 500; // ms before buffer clears

        function getVisibleOptions() {
            return Array.from(dropdown.querySelectorAll(optionSelector)).filter(function(opt) {
                return opt.offsetParent !== null && 
                       window.getComputedStyle(opt).display !== 'none';
            });
        }

        function isOpen() {
            return dropdown.style.display === 'block' ||
                   dropdown.classList.contains('show') ||
                   dropdown.classList.contains('is-open') ||
                   trigger.getAttribute('aria-expanded') === 'true';
        }

        function setFocusedOption(index) {
            options = getVisibleOptions();
            
            // Remove highlight from all
            options.forEach(function(opt) {
                opt.classList.remove('is-focused', 'keyboard-focused');
                opt.setAttribute('aria-selected', 'false');
            });

            focusedIndex = index;

            if (index >= 0 && index < options.length) {
                var option = options[index];
                option.classList.add('is-focused', 'keyboard-focused');
                option.setAttribute('aria-selected', 'true');
                option.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
                
                // Update aria-activedescendant on trigger if it has one
                if (trigger.hasAttribute('aria-activedescendant')) {
                    trigger.setAttribute('aria-activedescendant', option.id || '');
                }
            }
        }

        function closeDropdown() {
            dropdown.style.display = 'none';
            dropdown.classList.remove('show', 'is-open');
            trigger.setAttribute('aria-expanded', 'false');
            focusedIndex = -1;
            setFocusedOption(-1);
            onClose();
        }

        function openDropdown() {
            dropdown.style.display = 'block';
            dropdown.classList.add('show');
            trigger.setAttribute('aria-expanded', 'true');
            options = getVisibleOptions();
            focusedIndex = -1;
        }

        /**
         * TypeAhead: Jump to option starting with typed characters
         */
        function handleTypeAhead(char) {
            if (!isOpen()) return false;

            // Clear timeout and add to buffer
            if (typeAheadTimeout) {
                clearTimeout(typeAheadTimeout);
            }
            typeAheadBuffer += char.toLowerCase();

            // Set timeout to clear buffer
            typeAheadTimeout = setTimeout(function() {
                typeAheadBuffer = '';
            }, TYPE_AHEAD_DELAY);

            // Find matching option
            options = getVisibleOptions();
            var startIdx = focusedIndex >= 0 ? focusedIndex + 1 : 0;

            // Search from current position to end
            for (var i = startIdx; i < options.length; i++) {
                var text = (options[i].textContent || '').trim().toLowerCase();
                if (text.startsWith(typeAheadBuffer)) {
                    setFocusedOption(i);
                    return true;
                }
            }

            // Wrap around and search from beginning
            for (var j = 0; j < startIdx; j++) {
                var text2 = (options[j].textContent || '').trim().toLowerCase();
                if (text2.startsWith(typeAheadBuffer)) {
                    setFocusedOption(j);
                    return true;
                }
            }

            return false;
        }

        function selectCurrentOption() {
            options = getVisibleOptions();
            if (focusedIndex >= 0 && focusedIndex < options.length) {
                var selectedOption = options[focusedIndex];
                onSelect(selectedOption);
                if (closeOnSelect) {
                    closeDropdown();
                    trigger.focus();
                }
            }
        }

        // Keyboard handler for trigger
        trigger.addEventListener('keydown', function(e) {
            var open = isOpen();

            switch (e.key) {
                case 'Enter':
                case ' ':
                    e.preventDefault();
                    if (!open) {
                        openDropdown();
                    } else if (focusedIndex >= 0) {
                        selectCurrentOption();
                    }
                    break;

                case 'ArrowDown':
                    e.preventDefault();
                    if (!open) {
                        openDropdown();
                    }
                    options = getVisibleOptions();
                    if (options.length > 0) {
                        var newIndex = focusedIndex < options.length - 1 ? focusedIndex + 1 : 0;
                        setFocusedOption(newIndex);
                    }
                    break;

                case 'ArrowUp':
                    e.preventDefault();
                    if (open) {
                        options = getVisibleOptions();
                        if (options.length > 0) {
                            var prevIndex = focusedIndex > 0 ? focusedIndex - 1 : options.length - 1;
                            setFocusedOption(prevIndex);
                        }
                    }
                    break;

                case 'Escape':
                    if (open) {
                        e.preventDefault();
                        e.stopPropagation();
                        closeDropdown();
                        trigger.focus();
                    }
                    break;

                case 'Home':
                    if (open) {
                        e.preventDefault();
                        options = getVisibleOptions();
                        if (options.length > 0) {
                            setFocusedOption(0);
                        }
                    }
                    break;

                case 'End':
                    if (open) {
                        e.preventDefault();
                        options = getVisibleOptions();
                        if (options.length > 0) {
                            setFocusedOption(options.length - 1);
                        }
                    }
                    break;

                case 'Tab':
                    if (open) {
                        closeDropdown();
                    }
                    break;

                default:
                    // TypeAhead: handle printable characters
                    if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
                        if (handleTypeAhead(e.key)) {
                            e.preventDefault();
                        }
                    }
                    break;
            }
        });

        // Close on outside click
        document.addEventListener('click', function(e) {
            if (isOpen() && !trigger.contains(e.target) && !dropdown.contains(e.target)) {
                closeDropdown();
            }
        });

        // Mouse hover updates focused index
        dropdown.addEventListener('mouseover', function(e) {
            var option = e.target.closest(optionSelector);
            if (option) {
                options = getVisibleOptions();
                var idx = options.indexOf(option);
                if (idx >= 0) {
                    setFocusedOption(idx);
                }
            }
        });

        // Click on option
        dropdown.addEventListener('click', function(e) {
            var option = e.target.closest(optionSelector);
            if (option) {
                options = getVisibleOptions();
                focusedIndex = options.indexOf(option);
                selectCurrentOption();
            }
        });

        return {
            close: closeDropdown,
            open: openDropdown,
            refresh: function() {
                options = getVisibleOptions();
            }
        };
    }

    // ============= ACTION MENU KEYBOARD NAVIGATION =============

    /**
     * Initialize keyboard navigation for an action/context menu
     * @param {Object} config - Configuration object
     * @param {HTMLElement} config.menu - The menu container
     * @param {string} config.itemSelector - CSS selector for menu items
     * @param {Function} config.onClose - Callback when menu closes
     */
    function initMenuKeyboardNav(config) {
        var menu = config.menu;
        var itemSelector = config.itemSelector || '[role="menuitem"], .action-menu-item';
        var onClose = config.onClose || function() {};

        var focusedIndex = -1;

        // TypeAhead support
        var typeAheadBuffer = '';
        var typeAheadTimeout = null;
        var TYPE_AHEAD_DELAY = 500;

        function getVisibleItems() {
            return Array.from(menu.querySelectorAll(itemSelector)).filter(function(item) {
                return item.offsetParent !== null &&
                       window.getComputedStyle(item).display !== 'none';
            });
        }

        function isOpen() {
            return menu.style.display === 'block' ||
                   menu.classList.contains('show') ||
                   menu.classList.contains('is-open');
        }

        function setFocusedItem(index) {
            var items = getVisibleItems();

            items.forEach(function(item) {
                item.classList.remove('is-focused', 'keyboard-focused');
                item.setAttribute('tabindex', '-1');
            });

            focusedIndex = index;

            if (index >= 0 && index < items.length) {
                var item = items[index];
                item.classList.add('is-focused', 'keyboard-focused');
                item.setAttribute('tabindex', '0');
                item.focus();
            }
        }

        function closeMenu() {
            menu.style.display = 'none';
            menu.classList.remove('show', 'is-open');
            focusedIndex = -1;
            onClose();
        }

        function openMenu() {
            menu.style.display = 'block';
            menu.classList.add('show');
            // Focus first item
            setTimeout(function() {
                setFocusedItem(0);
            }, 50);
        }

        /**
         * TypeAhead: Jump to menu item starting with typed characters
         */
        function handleTypeAhead(char) {
            if (!isOpen()) return false;

            if (typeAheadTimeout) {
                clearTimeout(typeAheadTimeout);
            }
            typeAheadBuffer += char.toLowerCase();

            typeAheadTimeout = setTimeout(function() {
                typeAheadBuffer = '';
            }, TYPE_AHEAD_DELAY);

            var items = getVisibleItems();
            var startIdx = focusedIndex >= 0 ? focusedIndex + 1 : 0;

            // Search from current position
            for (var i = startIdx; i < items.length; i++) {
                var text = (items[i].textContent || '').trim().toLowerCase();
                if (text.startsWith(typeAheadBuffer)) {
                    setFocusedItem(i);
                    return true;
                }
            }

            // Wrap around
            for (var j = 0; j < startIdx; j++) {
                var text2 = (items[j].textContent || '').trim().toLowerCase();
                if (text2.startsWith(typeAheadBuffer)) {
                    setFocusedItem(j);
                    return true;
                }
            }

            return false;
        }

        menu.addEventListener('keydown', function(e) {
            if (!isOpen()) return;

            var items = getVisibleItems();

            switch (e.key) {
                case 'ArrowDown':
                    e.preventDefault();
                    if (items.length > 0) {
                        var nextIndex = focusedIndex < items.length - 1 ? focusedIndex + 1 : 0;
                        setFocusedItem(nextIndex);
                    }
                    break;

                case 'ArrowUp':
                    e.preventDefault();
                    if (items.length > 0) {
                        var prevIndex = focusedIndex > 0 ? focusedIndex - 1 : items.length - 1;
                        setFocusedItem(prevIndex);
                    }
                    break;

                case 'Enter':
                case ' ':
                    e.preventDefault();
                    if (focusedIndex >= 0 && focusedIndex < items.length) {
                        items[focusedIndex].click();
                    }
                    break;

                case 'Escape':
                    e.preventDefault();
                    e.stopPropagation();
                    closeMenu();
                    break;

                case 'Home':
                    e.preventDefault();
                    if (items.length > 0) {
                        setFocusedItem(0);
                    }
                    break;

                case 'End':
                    e.preventDefault();
                    if (items.length > 0) {
                        setFocusedItem(items.length - 1);
                    }
                    break;

                case 'Tab':
                    closeMenu();
                    break;

                default:
                    // TypeAhead: handle printable characters
                    if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
                        if (handleTypeAhead(e.key)) {
                            e.preventDefault();
                        }
                    }
                    break;
            }
        });

        // Mouse hover updates focus
        menu.addEventListener('mouseover', function(e) {
            var item = e.target.closest(itemSelector);
            if (item) {
                var items = getVisibleItems();
                var idx = items.indexOf(item);
                if (idx >= 0) {
                    setFocusedItem(idx);
                }
            }
        });

        return {
            close: closeMenu,
            open: openMenu
        };
    }

    // ============= CALENDAR GRID NAVIGATION =============

    /**
     * Initialize keyboard navigation for a calendar grid
     * @param {Object} config - Configuration object
     * @param {HTMLElement} config.grid - The calendar grid container
     * @param {string} config.cellSelector - CSS selector for day cells
     * @param {Function} config.onNavigate - Callback when navigating to a cell (receives cell element)
     * @param {Function} config.onSelect - Callback when selecting a cell (receives cell element)
     */
    function initCalendarKeyboardNav(config) {
        var grid = config.grid;
        var cellSelector = config.cellSelector || '.calendar-day, .day-cell, [data-date]';
        var onNavigate = config.onNavigate || function() {};
        var onSelect = config.onSelect || function() {};

        var focusedCell = null;

        function getCells() {
            return Array.from(grid.querySelectorAll(cellSelector)).filter(function(cell) {
                return cell.offsetParent !== null &&
                       !cell.classList.contains('empty') &&
                       !cell.classList.contains('disabled');
            });
        }

        function getCellPosition(cell) {
            var cells = getCells();
            var index = cells.indexOf(cell);
            // Assuming 7 columns (days of week)
            var row = Math.floor(index / 7);
            var col = index % 7;
            return { index: index, row: row, col: col };
        }

        function getCellAtPosition(row, col) {
            var cells = getCells();
            var index = row * 7 + col;
            return cells[index] || null;
        }

        function setFocusedCell(cell) {
            // Remove focus from previous
            if (focusedCell) {
                focusedCell.classList.remove('keyboard-focused');
                focusedCell.setAttribute('tabindex', '-1');
            }

            focusedCell = cell;

            if (cell) {
                cell.classList.add('keyboard-focused');
                cell.setAttribute('tabindex', '0');
                cell.focus();
                cell.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
                onNavigate(cell);
            }
        }

        function navigateByOffset(rowOffset, colOffset) {
            if (!focusedCell) {
                var cells = getCells();
                if (cells.length > 0) {
                    setFocusedCell(cells[0]);
                }
                return;
            }

            var pos = getCellPosition(focusedCell);
            var newRow = pos.row + rowOffset;
            var newCol = pos.col + colOffset;

            // Handle wrapping
            var cells = getCells();
            var totalRows = Math.ceil(cells.length / 7);

            if (newCol < 0) {
                newCol = 6;
                newRow--;
            } else if (newCol > 6) {
                newCol = 0;
                newRow++;
            }

            if (newRow < 0 || newRow >= totalRows) {
                // Would need to navigate to prev/next month
                // Let the caller handle this via onNavigate
                return;
            }

            var newCell = getCellAtPosition(newRow, newCol);
            if (newCell) {
                setFocusedCell(newCell);
            }
        }

        grid.addEventListener('keydown', function(e) {
            // Only handle if focus is within the grid
            if (!grid.contains(document.activeElement)) return;

            switch (e.key) {
                case 'ArrowRight':
                    e.preventDefault();
                    navigateByOffset(0, 1);
                    break;

                case 'ArrowLeft':
                    e.preventDefault();
                    navigateByOffset(0, -1);
                    break;

                case 'ArrowDown':
                    e.preventDefault();
                    navigateByOffset(1, 0);
                    break;

                case 'ArrowUp':
                    e.preventDefault();
                    navigateByOffset(-1, 0);
                    break;

                case 'Enter':
                case ' ':
                    e.preventDefault();
                    if (focusedCell) {
                        onSelect(focusedCell);
                    }
                    break;

                case 'Home':
                    e.preventDefault();
                    var cells = getCells();
                    if (cells.length > 0) {
                        setFocusedCell(cells[0]);
                    }
                    break;

                case 'End':
                    e.preventDefault();
                    var allCells = getCells();
                    if (allCells.length > 0) {
                        setFocusedCell(allCells[allCells.length - 1]);
                    }
                    break;
            }
        });

        // Click sets focus
        grid.addEventListener('click', function(e) {
            var cell = e.target.closest(cellSelector);
            if (cell && getCells().includes(cell)) {
                setFocusedCell(cell);
            }
        });

        // Make first cell focusable by default
        var initialCells = getCells();
        if (initialCells.length > 0) {
            initialCells[0].setAttribute('tabindex', '0');
        }

        return {
            setFocus: setFocusedCell,
            refresh: function() {
                var cells = getCells();
                if (cells.length > 0 && !focusedCell) {
                    cells[0].setAttribute('tabindex', '0');
                }
            }
        };
    }

    // ============= GLOBAL OVERLAY ESC HANDLER =============

    /**
     * Register an overlay to be closed on ESC
     * @param {HTMLElement} overlay - The overlay element
     * @param {Function} closeHandler - Function to close the overlay
     * @param {number} priority - Higher priority overlays close first (default: 0)
     */
    var registeredOverlays = [];

    function registerOverlayEsc(overlay, closeHandler, priority) {
        registeredOverlays.push({
            element: overlay,
            close: closeHandler,
            priority: priority || 0
        });

        // Sort by priority descending
        registeredOverlays.sort(function(a, b) {
            return b.priority - a.priority;
        });
    }

    function unregisterOverlayEsc(overlay) {
        registeredOverlays = registeredOverlays.filter(function(item) {
            return item.element !== overlay;
        });
    }

    // Global ESC handler
    document.addEventListener('keydown', function(e) {
        if (e.key !== 'Escape') return;

        // Find the first visible overlay
        for (var i = 0; i < registeredOverlays.length; i++) {
            var item = registeredOverlays[i];
            var el = item.element;

            // Check if visible
            var isVisible = el.offsetParent !== null ||
                           el.style.display === 'block' ||
                           el.style.display === 'flex' ||
                           el.classList.contains('show') ||
                           el.classList.contains('is-open') ||
                           el.classList.contains('active') ||
                           el.classList.contains('modal--open');

            if (isVisible) {
                e.preventDefault();
                e.stopPropagation();
                item.close();
                return;
            }
        }
    });

    // ============= CHECKBOX SPACE TOGGLE =============

    /**
     * Ensure Space key toggles checkboxes (browser default, but ensure custom checkboxes work)
     */
    document.addEventListener('keydown', function(e) {
        if (e.key !== ' ') return;

        var target = e.target;

        // Handle custom checkbox elements
        if (target.getAttribute('role') === 'checkbox') {
            e.preventDefault();
            var isChecked = target.getAttribute('aria-checked') === 'true';
            target.setAttribute('aria-checked', isChecked ? 'false' : 'true');
            target.classList.toggle('is-checked', !isChecked);

            // Dispatch change event
            target.dispatchEvent(new CustomEvent('change', {
                bubbles: true,
                detail: { checked: !isChecked }
            }));
        }

        // Handle custom switch elements
        if (target.getAttribute('role') === 'switch') {
            e.preventDefault();
            var isSwitchOn = target.getAttribute('aria-checked') === 'true';
            target.setAttribute('aria-checked', isSwitchOn ? 'false' : 'true');
            target.classList.toggle('is-on', !isSwitchOn);

            target.dispatchEvent(new CustomEvent('change', {
                bubbles: true,
                detail: { checked: !isSwitchOn }
            }));
        }
    });

    // ============= ENTER ACTIVATES BUTTONS/LINKS =============

    /**
     * Ensure Enter key activates buttons and links (for custom elements)
     */
    document.addEventListener('keydown', function(e) {
        if (e.key !== 'Enter') return;

        var target = e.target;

        // Handle elements with button role but not actual buttons
        if (target.getAttribute('role') === 'button' && target.tagName !== 'BUTTON') {
            e.preventDefault();
            target.click();
        }

        // Handle elements with link role but not actual links
        if (target.getAttribute('role') === 'link' && target.tagName !== 'A') {
            e.preventDefault();
            target.click();
        }
    });

    // ============= AUTO-INITIALIZATION =============

    /**
     * Auto-initialize dropdowns with proper ARIA attributes
     */
    function autoInitDropdowns() {
        // Find all comboboxes/triggers with dropdowns
        var triggers = document.querySelectorAll('[role="combobox"], [aria-haspopup="listbox"]');

        triggers.forEach(function(trigger) {
            // Find associated dropdown
            var dropdownId = trigger.getAttribute('aria-controls') ||
                            trigger.getAttribute('aria-owns');
            var dropdown = dropdownId ? document.getElementById(dropdownId) : null;

            // Try to find dropdown as sibling
            if (!dropdown) {
                dropdown = trigger.nextElementSibling;
                if (dropdown && !dropdown.matches('[role="listbox"], .dropdown, .custom-dropdown')) {
                    dropdown = trigger.parentElement.querySelector('[role="listbox"], .dropdown, .custom-dropdown');
                }
            }

            if (dropdown && !trigger._keyboardNavInitialized) {
                trigger._keyboardNavInitialized = true;
                initDropdownKeyboardNav({
                    trigger: trigger,
                    dropdown: dropdown,
                    onSelect: function(option) {
                        // Try to find existing click handler
                        if (option.onclick) {
                            option.onclick.call(option);
                        } else {
                            option.click();
                        }
                    }
                });
            }
        });

        // Find all menus
        var menus = document.querySelectorAll('[role="menu"], .action-menu');

        menus.forEach(function(menu) {
            if (!menu._keyboardNavInitialized) {
                menu._keyboardNavInitialized = true;
                initMenuKeyboardNav({
                    menu: menu
                });
            }
        });

        // Find all calendar grids
        var grids = document.querySelectorAll('.calendar-grid');

        grids.forEach(function(grid) {
            if (!grid._keyboardNavInitialized) {
                grid._keyboardNavInitialized = true;
                initCalendarKeyboardNav({
                    grid: grid,
                    cellSelector: '.calendar-day, .day-cell, [data-date]',
                    onSelect: function(cell) {
                        // Try to find clickable element within cell
                        var clickable = cell.querySelector('button, a, [onclick]');
                        if (clickable) {
                            clickable.click();
                        } else if (cell.onclick) {
                            cell.onclick.call(cell);
                        } else {
                            cell.click();
                        }
                    }
                });
            }
        });
    }

    // Run on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', autoInitDropdowns);
    } else {
        autoInitDropdowns();
    }

    // Also run after dynamic content loads
    if (typeof MutationObserver !== 'undefined') {
        var observer = new MutationObserver(function(mutations) {
            var shouldInit = mutations.some(function(mutation) {
                return mutation.addedNodes.length > 0;
            });
            if (shouldInit) {
                // Debounce
                clearTimeout(observer._timeout);
                observer._timeout = setTimeout(autoInitDropdowns, 100);
            }
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        // Clean up on page unload to prevent memory leaks
        window.addEventListener('beforeunload', function() {
            clearTimeout(observer._timeout);
            observer.disconnect();
        });
    }

    // ============= EXPOSE API =============

    window.KeyboardNav = {
        initDropdown: initDropdownKeyboardNav,
        initMenu: initMenuKeyboardNav,
        initCalendar: initCalendarKeyboardNav,
        registerOverlay: registerOverlayEsc,
        unregisterOverlay: unregisterOverlayEsc,
        autoInit: autoInitDropdowns
    };
})();
