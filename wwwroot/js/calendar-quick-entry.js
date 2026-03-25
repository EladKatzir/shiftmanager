(function () {
    'use strict';

    // === State ===
    var isActive = false;
    var activeCell = null;
    var activeInput = null;
    var dropdown = null;
    var selectedIndex = -1;
    var allItems = [];
    var filteredItems = [];
    var isComposing = false;
    var slashCommand = null;
    var choreTypeForTitle = null;
    var promptElement = null;
    var promptResolve = null;
    var promptTimer = null;
    var tooltipElement = null;
    var tooltipTimer = null;
    var tooltipCount = parseInt(localStorage.getItem('qe-tooltip-count') || '0', 10);
    var scrollHandler = null;
    var domObserver = null;
    var savedInputState = null; // { rowId, date, value, selectionStart }

    // === Constants ===
    var MAX_RESULTS_PER_GROUP = 6;
    var TOOLTIP_MAX_SHOWS = 3;
    var LS_KEY_ACTIVE = 'quickEntryActive';
    var LS_KEY_TOOLTIP = 'qe-tooltip-count';

    // Hebrew final-form normalization map
    var HEBREW_FINAL_MAP = {
        '\u05DA': '\u05DB', // final kaf -> kaf
        '\u05DD': '\u05DE', // final mem -> mem
        '\u05DF': '\u05E0', // final nun -> nun
        '\u05E3': '\u05E4', // final pe -> pe
        '\u05E5': '\u05E6'  // final tsadi -> tsadi
    };

    // Slash commands definition
    var SLASH_COMMANDS = [
        { key: 'shift', icon: '\uD83D\uDCC5', labelKey: 'QuickEntry_ShiftsGroup', modes: ['user'] },
        { key: 'chore', icon: '\uD83E\uDDF9', labelKey: 'QuickEntry_ChoresGroup', modes: ['user'] },
        { key: 'duty',  icon: '\uD83D\uDEE1', labelKey: 'QuickEntry_DutyGroup',   modes: ['user'] },
        { key: 'home',  icon: '\uD83C\uDFE0', label: 'HOME',                       modes: ['user', 'shift'] }
    ];

    // --- Helpers ---

    function getCulture() {
        var lang = document.documentElement.lang || 'en-US';
        return lang.startsWith('he') ? 'he-IL' : 'en-US';
    }

    function getLocalizedLabel(key) {
        if (window.AppLocalizer && window.AppLocalizer[key]) return window.AppLocalizer[key];
        var isHe = getCulture() === 'he-IL';
        var labels = {
            'QuickEntry_ShiftsGroup': isHe ? '\u05DE\u05E9\u05DE\u05E8\u05D5\u05EA' : 'Shifts',
            'QuickEntry_ChoresGroup': isHe ? '\u05EA\u05D5\u05E8\u05E0\u05D5\u05D9\u05D5\u05EA' : 'Chores',
            'QuickEntry_DutyGroup': isHe ? '\u05DB\u05D5\u05E0\u05E0\u05D9\u05D5\u05EA' : 'Day Shifts',
            'QuickEntry_NoMatches': isHe ? '\u05D0\u05D9\u05DF \u05EA\u05D5\u05E6\u05D0\u05D5\u05EA' : 'No matches',
            'QuickEntry_Tooltip': isHe
                ? '\u05D4\u05E7\u05DC\u05D3 \u05DC\u05E9\u05D9\u05D1\u05D5\u05E5, Tab \u05DC\u05DE\u05E2\u05D1\u05E8 \u05D9\u05DE\u05D9\u05E0\u05D4, Escape \u05DC\u05D1\u05D9\u05D8\u05D5\u05DC. \u05D4\u05E7\u05DC\u05D3 / \u05DC\u05EA\u05D5\u05E8\u05E0\u05D5\u05D9\u05D5\u05EA \u05D5\u05E2\u05D5\u05D3.'
                : 'Type to assign, Tab to move right, Escape to cancel. Use / for chores and more.',
            'QuickEntry_DisabledJustMine': isHe
                ? '\u05D1\u05D8\u05DC "\u05E8\u05E7 \u05E9\u05DC\u05D9" \u05DB\u05D3\u05D9 \u05DC\u05D4\u05E9\u05EA\u05DE\u05E9 \u05D1\u05D4\u05E7\u05E6\u05D0\u05D4 \u05DE\u05D4\u05D9\u05E8\u05D4'
                : 'Disable Just Mine to use Quick Entry',
            'QuickEntry_TitlePlaceholder': isHe ? '\u05DB\u05D5\u05EA\u05E8\u05EA...' : 'Title...',
            'QuickEntry_EnterYesEscNo': isHe ? 'Enter=\u05DB\u05DF, Esc=\u05DC\u05D0' : 'Enter=yes, Esc=no'
        };
        return labels[key] || key;
    }

    function getCurrentMode() {
        var sel = document.querySelector('[data-role="assignee-select"]');
        if (!sel) return null;
        return sel.dataset.itemType === 'user' ? 'shift' : 'user';
    }

    function isJustMineActive() {
        return window.location.search.indexOf('JustMine=True') !== -1;
    }

    // --- Hebrew normalization & fuzzy match ---

    function normalizeHebrew(str) {
        var result = '';
        for (var i = 0; i < str.length; i++) {
            result += HEBREW_FINAL_MAP[str[i]] || str[i];
        }
        return result.toLowerCase();
    }

    function fuzzyMatch(query, text) {
        if (!query) return { match: true, score: 0 };

        var normQuery = normalizeHebrew(query);
        var normText = normalizeHebrew(text);

        if (normText.startsWith(normQuery)) return { match: true, score: 100 };

        var words = normText.split(/\s+/);
        for (var w = 0; w < words.length; w++) {
            if (words[w].startsWith(normQuery)) return { match: true, score: 80 };
        }

        if (normText.indexOf(normQuery) !== -1) return { match: true, score: 60 };

        var qi = 0;
        for (var ti = 0; ti < normText.length && qi < normQuery.length; ti++) {
            if (normText[ti] === normQuery[qi]) qi++;
        }
        if (qi === normQuery.length) return { match: true, score: 40 };

        return { match: false, score: 0 };
    }

    // --- Item loading ---

    function loadItems() {
        allItems = [];
        var assigneeSelect = document.querySelector('[data-role="assignee-select"]');
        if (!assigneeSelect) return;

        var itemType = assigneeSelect.dataset.itemType;

        var options = assigneeSelect.querySelectorAll('option');
        for (var i = 0; i < options.length; i++) {
            var opt = options[i];
            if (!opt.value) continue;
            allItems.push({
                id: opt.value,
                text: opt.textContent.trim(),
                type: itemType === 'user' ? 'user' : 'shift',
                key: opt.dataset.key || null,
                color: null
            });
        }

        if (itemType === 'shifttype') {
            var choreOpts = document.querySelectorAll('[data-role="quickentry-choretype"] option');
            for (var c = 0; c < choreOpts.length; c++) {
                var co = choreOpts[c];
                if (!co.value) continue;
                allItems.push({
                    id: co.value,
                    text: co.textContent.trim(),
                    type: 'chore',
                    color: co.dataset.color || null
                });
            }

            var dutyOpts = document.querySelectorAll('[data-role="quickentry-dutytype"] option');
            for (var d = 0; d < dutyOpts.length; d++) {
                var dopt = dutyOpts[d];
                if (!dopt.value) continue;
                allItems.push({
                    id: dopt.value,
                    text: dopt.textContent.trim(),
                    type: 'duty',
                    icon: dopt.dataset.icon || null,
                    color: dopt.dataset.color || null
                });
            }
        }
    }

    // --- Filter & group ---

    function filterAndGroup(query) {
        var groups = { shift: [], chore: [], duty: [], user: [] };
        var overflow = { shift: 0, chore: 0, duty: 0, user: 0 };
        var mode = getCurrentMode();

        for (var i = 0; i < allItems.length; i++) {
            var item = allItems[i];

            // Slash command filtering
            if (slashCommand) {
                if (slashCommand === 'home') {
                    if (item.type !== 'shift' || item.key !== 'HOME') continue;
                } else if (item.type !== slashCommand) {
                    continue;
                }
            } else if (mode === 'shift') {
                // Shift mode: only show users
                if (item.type !== 'user') continue;
            }

            var result = fuzzyMatch(query, item.text);
            if (!result.match) continue;

            var group = groups[item.type];
            if (!group) continue;

            if (group.length < MAX_RESULTS_PER_GROUP) {
                group.push({ item: item, score: result.score });
            } else {
                overflow[item.type]++;
            }
        }

        // Sort each group by score descending
        var keys = ['user', 'shift', 'chore', 'duty'];
        for (var k = 0; k < keys.length; k++) {
            groups[keys[k]].sort(function (a, b) { return b.score - a.score; });
        }

        return { groups: groups, overflow: overflow };
    }

    // --- Dropdown rendering ---

    function createDropdown() {
        if (dropdown) return dropdown;
        dropdown = document.createElement('div');
        dropdown.className = 'quick-entry-dropdown';
        dropdown.setAttribute('role', 'listbox');
        dropdown.id = 'qe-dropdown-' + Date.now();
        document.body.appendChild(dropdown);

        dropdown.addEventListener('mousedown', function (e) {
            e.preventDefault(); // prevent blur on input
        });

        return dropdown;
    }

    function removeDropdown() {
        if (dropdown) {
            dropdown.remove();
            dropdown = null;
        }
        selectedIndex = -1;
        filteredItems = [];
    }

    function positionDropdown() {
        if (!dropdown || !activeInput) return;
        var rect = activeInput.getBoundingClientRect();
        var dropHeight = dropdown.offsetHeight || 200;
        var viewportHeight = window.innerHeight;
        var spaceBelow = viewportHeight - rect.bottom - 8;
        var spaceAbove = rect.top - 8;

        var top;
        if (spaceBelow >= dropHeight || spaceBelow >= spaceAbove) {
            top = rect.bottom + 2;
        } else {
            top = rect.top - dropHeight - 2;
        }

        var left = rect.left;
        var dropWidth = dropdown.offsetWidth || 200;
        if (left + dropWidth > window.innerWidth - 8) {
            left = window.innerWidth - dropWidth - 8;
        }
        if (left < 8) left = 8;

        dropdown.style.top = top + 'px';
        dropdown.style.left = left + 'px';
    }

    function updateDropdown(query) {
        if (!activeInput) return;

        createDropdown();
        dropdown.innerHTML = '';
        filteredItems = [];
        selectedIndex = -1;

        var result = filterAndGroup(query);
        var groups = result.groups;
        var overflow = result.overflow;
        var mode = getCurrentMode();

        var groupOrder = mode === 'shift' ? ['user'] : ['shift', 'chore', 'duty'];
        if (slashCommand) {
            if (slashCommand === 'home') groupOrder = ['shift'];
            else groupOrder = [slashCommand];
        }

        var totalItems = 0;
        var showHeaders = groupOrder.length > 1;

        for (var g = 0; g < groupOrder.length; g++) {
            var groupKey = groupOrder[g];
            var items = groups[groupKey];
            if (!items || items.length === 0) continue;

            if (showHeaders) {
                var header = document.createElement('div');
                header.className = 'quick-entry-group-header';
                header.setAttribute('aria-hidden', 'true');

                var iconMap = { shift: '\uD83D\uDCC5', chore: '\uD83E\uDDF9', duty: '\uD83D\uDEE1', user: '' };
                var labelMap = { shift: 'QuickEntry_ShiftsGroup', chore: 'QuickEntry_ChoresGroup', duty: 'QuickEntry_DutyGroup' };

                if (iconMap[groupKey]) {
                    var iconSpan = document.createElement('span');
                    iconSpan.textContent = iconMap[groupKey];
                    header.appendChild(iconSpan);
                }
                if (labelMap[groupKey]) {
                    var labelSpan = document.createElement('span');
                    labelSpan.textContent = getLocalizedLabel(labelMap[groupKey]);
                    header.appendChild(labelSpan);
                }
                dropdown.appendChild(header);
            }

            for (var i = 0; i < items.length; i++) {
                var entry = items[i];
                var idx = filteredItems.length;
                filteredItems.push(entry.item);

                var el = document.createElement('div');
                el.className = 'quick-entry-item';
                el.setAttribute('role', 'option');
                el.id = dropdown.id + '-item-' + idx;
                el.dataset.index = idx;

                if (entry.item.color) {
                    var colorDot = document.createElement('span');
                    colorDot.style.cssText = 'display:inline-block;width:10px;height:10px;border-radius:50%;background:' + entry.item.color + ';flex-shrink:0;';
                    el.appendChild(colorDot);
                }

                var textSpan = document.createElement('span');
                textSpan.textContent = entry.item.text;
                el.appendChild(textSpan);

                (function (capturedIdx) {
                    el.addEventListener('mousedown', function (e) {
                        e.preventDefault();
                        selectedIndex = capturedIdx;
                        selectItem(filteredItems[capturedIdx]);
                    });
                })(idx);

                dropdown.appendChild(el);
                totalItems++;
            }

            if (overflow[groupKey] > 0) {
                var more = document.createElement('div');
                more.className = 'quick-entry-overflow';
                more.textContent = '+' + overflow[groupKey] + ' more...';
                dropdown.appendChild(more);
            }
        }

        if (totalItems === 0) {
            var noMatch = document.createElement('div');
            noMatch.className = 'quick-entry-no-matches';
            noMatch.textContent = getLocalizedLabel('QuickEntry_NoMatches');
            dropdown.appendChild(noMatch);
        }

        activeInput.setAttribute('aria-expanded', 'true');
        activeInput.setAttribute('aria-controls', dropdown.id);
        positionDropdown();
    }

    function showSlashPalette(partial) {
        if (!activeInput) return;

        createDropdown();
        dropdown.innerHTML = '';
        filteredItems = [];
        selectedIndex = -1;

        var mode = getCurrentMode();
        var matchText = partial.substring(1).toLowerCase(); // strip leading /

        for (var i = 0; i < SLASH_COMMANDS.length; i++) {
            var cmd = SLASH_COMMANDS[i];
            if (cmd.modes.indexOf(mode) === -1) continue;
            if (matchText && cmd.key.indexOf(matchText) !== 0 && !(cmd.label && cmd.label.toLowerCase().indexOf(matchText) === 0)) continue;

            var idx = filteredItems.length;
            filteredItems.push({ _slashCmd: cmd.key, text: cmd.label || getLocalizedLabel(cmd.labelKey), type: '_command' });

            var el = document.createElement('div');
            el.className = 'quick-entry-item quick-entry-item--command';
            el.setAttribute('role', 'option');
            el.id = dropdown.id + '-item-' + idx;
            el.dataset.index = idx;

            var iconSpan = document.createElement('span');
            iconSpan.textContent = cmd.icon;
            el.appendChild(iconSpan);

            var labelSpan = document.createElement('span');
            labelSpan.textContent = cmd.label || getLocalizedLabel(cmd.labelKey);
            el.appendChild(labelSpan);

            var shortcut = document.createElement('span');
            shortcut.className = 'quick-entry-item__shortcut';
            shortcut.textContent = '/' + cmd.key;
            el.appendChild(shortcut);

            (function (capturedIdx) {
                el.addEventListener('mousedown', function (e) {
                    e.preventDefault();
                    selectSlashCommand(filteredItems[capturedIdx]);
                });
            })(idx);

            dropdown.appendChild(el);
        }

        if (filteredItems.length === 0) {
            var noMatch = document.createElement('div');
            noMatch.className = 'quick-entry-no-matches';
            noMatch.textContent = getLocalizedLabel('QuickEntry_NoMatches');
            dropdown.appendChild(noMatch);
        }

        activeInput.setAttribute('aria-expanded', 'true');
        activeInput.setAttribute('aria-controls', dropdown.id);
        positionDropdown();
    }

    function selectSlashCommand(cmdItem) {
        if (!activeInput || !cmdItem._slashCmd) return;
        slashCommand = cmdItem._slashCmd;

        // Add chip before input
        var chip = document.createElement('span');
        chip.className = 'quick-entry-chip quick-entry-chip--' + slashCommand;
        chip.textContent = cmdItem.text;
        activeInput.parentNode.insertBefore(chip, activeInput);
        activeInput._chip = chip;

        activeInput.value = '';
        activeInput.focus();
        updateDropdown('');
    }

    function moveSelection(delta) {
        if (filteredItems.length === 0) return;

        var items = dropdown ? dropdown.querySelectorAll('[role="option"]') : [];
        if (items.length === 0) return;

        // Remove current highlight
        if (selectedIndex >= 0 && items[selectedIndex]) {
            items[selectedIndex].classList.remove('quick-entry-item--active');
        }

        selectedIndex += delta;
        if (selectedIndex < 0) selectedIndex = items.length - 1;
        if (selectedIndex >= items.length) selectedIndex = 0;

        items[selectedIndex].classList.add('quick-entry-item--active');
        items[selectedIndex].scrollIntoView({ block: 'nearest' });

        activeInput.setAttribute('aria-activedescendant', items[selectedIndex].id);
    }

    // --- Cell click handler ---

    function handleCellClick(e) {
        if (!isActive) return;

        var cell = e.target.closest('.excel-calendar__cell');
        if (!cell) return;

        if (cell.classList.contains('excel-calendar__cell--readonly') ||
            cell.classList.contains('excel-calendar__cell--past')) {
            return;
        }

        // Don't intercept clicks on existing assignments or buttons
        if (e.target.closest('.excel-calendar__assignment') ||
            e.target.closest('.excel-calendar__add-btn') ||
            e.target.closest('.fill-handle') ||
            e.target.closest('button') ||
            e.target.closest('a')) {
            return;
        }

        e.preventDefault();
        e.stopPropagation();

        if (!window.CalendarBottomSheet || typeof window.CalendarBottomSheet.extractCellData !== 'function') {
            return;
        }

        var cellData = window.CalendarBottomSheet.extractCellData(cell);
        if (!cellData || !cellData.rowId || !cellData.date) return;

        openInput(cell, cellData);
    }

    // --- Input lifecycle ---

    function openInput(cell, cellData) {
        closeInput();
        activeCell = cell;
        cell.classList.add('quick-entry-active');

        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'quick-entry-input';
        input.dir = 'auto';
        input.autocomplete = 'off';
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-haspopup', 'listbox');
        input.setAttribute('aria-autocomplete', 'list');
        input.placeholder = '';

        cell.appendChild(input);
        input.focus();
        activeInput = input;

        input._cellData = cellData;

        if (allItems.length === 0) loadItems();

        input.addEventListener('input', handleInput);
        input.addEventListener('keydown', handleKeydown);
        input.addEventListener('compositionstart', function () { isComposing = true; });
        input.addEventListener('compositionend', function () {
            isComposing = false;
            handleInput();
        });
        input.addEventListener('blur', handleBlur);

        scrollHandler = function () { positionDropdown(); };
        window.addEventListener('scroll', scrollHandler, true);

        updateDropdown('');
    }

    function closeInput() {
        dismissPrompt(false);

        if (scrollHandler) {
            window.removeEventListener('scroll', scrollHandler, true);
            scrollHandler = null;
        }

        if (activeInput) {
            if (activeInput._chip) {
                activeInput._chip.remove();
                activeInput._chip = null;
            }
            activeInput.removeEventListener('input', handleInput);
            activeInput.removeEventListener('keydown', handleKeydown);
            activeInput.removeEventListener('blur', handleBlur);
            activeInput.remove();
            activeInput = null;
        }

        removeDropdown();

        if (activeCell) {
            activeCell.classList.remove('quick-entry-active');
            activeCell = null;
        }

        selectedIndex = -1;
        slashCommand = null;
        choreTypeForTitle = null;
    }

    // --- Input events ---

    function handleInput() {
        if (isComposing) return;
        if (!activeInput) return;

        savedInputState = saveInputState();
        var value = activeInput.value;

        if (choreTypeForTitle) {
            // In chore title mode, no dropdown needed
            removeDropdown();
            return;
        }

        if (value.startsWith('/') && !slashCommand) {
            showSlashPalette(value);
            return;
        }

        updateDropdown(value);
    }

    function handleKeydown(e) {
        if (isComposing) {
            if (e.key === 'Tab') e.preventDefault();
            return;
        }

        // If inline prompt is active, handle Enter/Esc for it
        if (promptResolve) {
            if (e.key === 'Enter') {
                e.preventDefault();
                dismissPrompt(true);
                return;
            }
            if (e.key === 'Escape') {
                e.preventDefault();
                dismissPrompt(false);
                return;
            }
            e.preventDefault();
            return;
        }

        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                moveSelection(1);
                break;
            case 'ArrowUp':
                e.preventDefault();
                moveSelection(-1);
                break;
            case 'Enter':
                e.preventDefault();
                if (choreTypeForTitle) {
                    commitChoreTitle();
                } else if (selectedIndex >= 0 && filteredItems[selectedIndex]) {
                    var item = filteredItems[selectedIndex];
                    if (item.type === '_command') {
                        selectSlashCommand(item);
                    } else {
                        selectItem(item);
                    }
                }
                break;
            case 'Tab':
                e.preventDefault();
                if (choreTypeForTitle) {
                    commitChoreTitle();
                } else if (selectedIndex >= 0 && filteredItems[selectedIndex]) {
                    var tabItem = filteredItems[selectedIndex];
                    if (tabItem.type === '_command') {
                        selectSlashCommand(tabItem);
                    } else {
                        selectItem(tabItem, true);
                    }
                } else {
                    advanceToNextCell();
                }
                break;
            case 'Escape':
                e.preventDefault();
                if (slashCommand && !choreTypeForTitle) {
                    // Clear slash command filter
                    if (activeInput._chip) {
                        activeInput._chip.remove();
                        activeInput._chip = null;
                    }
                    slashCommand = null;
                    activeInput.value = '';
                    updateDropdown('');
                } else {
                    var cellToFocus = activeCell;
                    closeInput();
                    if (cellToFocus) cellToFocus.focus();
                }
                break;
        }
    }

    function handleBlur() {
        setTimeout(function () {
            if (dropdown && dropdown.contains(document.activeElement)) return;
            if (activeInput && document.activeElement === activeInput) return;
            if (promptElement) return; // don't close during prompt
            closeInput();
        }, 200);
    }

    // --- Item selection ---

    function selectItem(item, advance) {
        if (!activeInput || !activeCell) return;

        var cellData = activeInput._cellData;
        var rowId = cellData.rowId;
        var date = cellData.date;

        var qeConfirm = function (msg) {
            return new Promise(function (resolve) {
                showInlinePrompt(msg, resolve);
            });
        };

        if (item.type === 'chore') {
            choreTypeForTitle = item;
            enterChoreTitle();
            return;
        }

        // Capture cell reference before async work
        var currentCell = activeCell;
        var doAdvance = !!advance;

        var promise;
        if (item.type === 'user') {
            var shiftTypeId = rowId.replace('shift-', '');
            promise = window.quickAddShift(parseInt(shiftTypeId, 10), date, parseInt(item.id, 10), qeConfirm);
        } else if (item.type === 'shift') {
            var userId = rowId.replace('user-', '');
            promise = window.quickAddShift(parseInt(item.id, 10), date, parseInt(userId, 10), qeConfirm);
        } else if (item.type === 'duty') {
            var dutyUserId = rowId.replace('user-', '');
            promise = window.quickAddOnDuty(date, parseInt(dutyUserId, 10), parseInt(item.id, 10), false, qeConfirm);
        }

        if (promise && typeof promise.then === 'function') {
            promise.then(function () {
                closeInput();
                if (doAdvance) advanceToNextCell(currentCell);
            });
        } else {
            closeInput();
            if (doAdvance) advanceToNextCell(currentCell);
        }
    }

    // --- Chore title entry ---

    function enterChoreTitle() {
        if (!activeInput || !choreTypeForTitle) return;

        // Remove any existing chip from slash command
        if (activeInput._chip) {
            activeInput._chip.remove();
            activeInput._chip = null;
        }

        removeDropdown();
        slashCommand = null;

        // Add chore type chip
        var chip = document.createElement('span');
        chip.className = 'quick-entry-chip quick-entry-chip--chore';
        if (choreTypeForTitle.color) {
            chip.style.background = choreTypeForTitle.color;
            chip.style.color = '#fff';
        }
        chip.textContent = choreTypeForTitle.text;
        activeInput.parentNode.insertBefore(chip, activeInput);
        activeInput._chip = chip;

        activeInput.value = '';
        activeInput.placeholder = getLocalizedLabel('QuickEntry_TitlePlaceholder');
        activeInput.focus();
    }

    function commitChoreTitle() {
        if (!activeInput || !choreTypeForTitle || !activeCell) return;

        var title = activeInput.value.trim();
        if (!title) {
            // If empty, cancel chore entry
            choreTypeForTitle = null;
            closeInput();
            return;
        }

        var cellData = activeInput._cellData;
        var rowId = cellData.rowId;
        var date = cellData.date;
        var userId = rowId.replace('user-', '');
        var choreTypeId = choreTypeForTitle.id;

        var qeConfirm = function (msg) {
            return new Promise(function (resolve) {
                showInlinePrompt(msg, resolve);
            });
        };

        choreTypeForTitle = null;
        window.quickAddChore(date, parseInt(userId, 10), title, false, parseInt(choreTypeId, 10), qeConfirm);
        closeInput();
    }

    // --- Inline prompt (replaces native confirm) ---

    function showInlinePrompt(msg, resolve) {
        dismissPrompt(false);

        promptResolve = resolve;

        promptElement = document.createElement('div');
        promptElement.className = 'quick-entry-prompt';
        promptElement.setAttribute('role', 'alert');

        var msgEl = document.createElement('div');
        msgEl.textContent = msg;
        promptElement.appendChild(msgEl);

        var hintEl = document.createElement('div');
        hintEl.style.cssText = 'margin-top:4px;font-size:0.6875rem;opacity:0.8;';
        hintEl.textContent = getLocalizedLabel('QuickEntry_EnterYesEscNo');
        promptElement.appendChild(hintEl);

        document.body.appendChild(promptElement);

        // Position near the input
        if (activeInput) {
            var rect = activeInput.getBoundingClientRect();
            promptElement.style.top = (rect.bottom + 4) + 'px';
            promptElement.style.left = rect.left + 'px';
        }

        promptTimer = setTimeout(function () {
            dismissPrompt(false);
        }, 10000);

        if (activeInput) activeInput.focus();
    }

    function dismissPrompt(result) {
        if (promptTimer) {
            clearTimeout(promptTimer);
            promptTimer = null;
        }
        if (promptElement) {
            promptElement.remove();
            promptElement = null;
        }
        if (promptResolve) {
            var fn = promptResolve;
            promptResolve = null;
            fn(result);
        }
    }

    // --- Cell advancement ---

    function advanceToNextCell(fromCell) {
        var cell = fromCell || activeCell;
        if (!cell) return;

        var row = cell.closest('tr');
        if (!row) return;

        var cells = row.querySelectorAll('.excel-calendar__cell');
        var currentIdx = -1;
        for (var i = 0; i < cells.length; i++) {
            if (cells[i] === cell) { currentIdx = i; break; }
        }

        if (currentIdx === -1) return;

        // Try next cell in same row
        var nextIdx = currentIdx + 1;
        if (nextIdx < cells.length) {
            var nextCell = cells[nextIdx];
            if (!nextCell.classList.contains('excel-calendar__cell--readonly') &&
                !nextCell.classList.contains('excel-calendar__cell--past')) {
                var cellData = window.CalendarBottomSheet.extractCellData(nextCell);
                if (cellData && cellData.rowId && cellData.date) {
                    openInput(nextCell, cellData);
                    return;
                }
            }
        }

        // Try first cell of next row
        var nextRow = row.nextElementSibling;
        while (nextRow) {
            var nextRowCells = nextRow.querySelectorAll('.excel-calendar__cell');
            for (var j = 0; j < nextRowCells.length; j++) {
                var candidate = nextRowCells[j];
                if (!candidate.classList.contains('excel-calendar__cell--readonly') &&
                    !candidate.classList.contains('excel-calendar__cell--past')) {
                    var nextCellData = window.CalendarBottomSheet.extractCellData(candidate);
                    if (nextCellData && nextCellData.rowId && nextCellData.date) {
                        openInput(candidate, nextCellData);
                        return;
                    }
                }
            }
            nextRow = nextRow.nextElementSibling;
        }

        // Nothing found, just close
        closeInput();
    }

    // --- First-use tooltip ---

    function showFirstUseTooltip() {
        if (tooltipCount >= TOOLTIP_MAX_SHOWS) return;

        var btn = document.getElementById('quickEntryToggle');
        if (!btn) return;

        dismissTooltip();

        tooltipElement = document.createElement('div');
        tooltipElement.className = 'quick-entry-tooltip';
        tooltipElement.textContent = getLocalizedLabel('QuickEntry_Tooltip');

        btn.style.position = btn.style.position || 'relative';
        var parent = btn.parentNode;
        if (parent) {
            parent.style.position = parent.style.position || 'relative';
            parent.appendChild(tooltipElement);
        }

        // Position below button
        var btnRect = btn.getBoundingClientRect();
        var parentRect = parent.getBoundingClientRect();
        tooltipElement.style.top = (btnRect.bottom - parentRect.top + 8) + 'px';
        tooltipElement.style.insetInlineStart = (btnRect.left - parentRect.left) + 'px';

        tooltipElement.addEventListener('click', function () {
            dismissTooltip();
        });

        tooltipCount++;
        localStorage.setItem(LS_KEY_TOOLTIP, String(tooltipCount));

        tooltipTimer = setTimeout(function () {
            dismissTooltip();
        }, 8000);
    }

    function dismissTooltip() {
        if (tooltipTimer) {
            clearTimeout(tooltipTimer);
            tooltipTimer = null;
        }
        if (tooltipElement) {
            tooltipElement.remove();
            tooltipElement = null;
        }
    }

    // --- SignalR DOM save/restore via MutationObserver ---

    function saveInputState() {
        if (!activeInput || !activeCell) return null;
        return {
            rowId: activeCell.dataset.rowId,
            date: activeCell.dataset.date,
            value: activeInput.value,
            selectionStart: activeInput.selectionStart,
            slashCommand: slashCommand,
            choreTypeForTitle: choreTypeForTitle
        };
    }

    function restoreInputState(state) {
        if (!state) return;
        var cell = document.querySelector(
            '.excel-calendar__cell[data-row-id="' + state.rowId + '"][data-date="' + state.date + '"]'
        );
        if (!cell) return;

        var cellData = window.CalendarBottomSheet.extractCellData(cell);
        slashCommand = state.slashCommand;
        choreTypeForTitle = state.choreTypeForTitle;
        openInput(cell, cellData);

        if (activeInput) {
            activeInput.value = state.value;
            if (typeof state.selectionStart === 'number') {
                activeInput.selectionStart = activeInput.selectionEnd = state.selectionStart;
            }
            updateDropdown(state.value);
        }
    }

    function setupDomObserver() {
        if (domObserver) return;
        var table = document.querySelector('.excel-calendar');
        if (!table) return;

        domObserver = new MutationObserver(function (mutations) {
            if (!activeInput || !activeCell) return;

            var inputLost = !document.body.contains(activeInput);
            if (inputLost) {
                var state = savedInputState;
                savedInputState = null;
                restoreInputState(state);
            }
        });

        domObserver.observe(table, { childList: true, subtree: true });
    }

    function teardownDomObserver() {
        if (domObserver) {
            domObserver.disconnect();
            domObserver = null;
        }
        savedInputState = null;
    }

    // --- Event listener management ---

    var calendarTable = null;

    function attachListeners() {
        calendarTable = document.querySelector('.excel-calendar');
        if (calendarTable) {
            calendarTable.addEventListener('click', handleCellClick);
        }
        setupDomObserver();
    }

    function detachListeners() {
        if (calendarTable) {
            calendarTable.removeEventListener('click', handleCellClick);
            calendarTable = null;
        }
        teardownDomObserver();
    }

    // --- Toggle ---

    function toggle() {
        if (isJustMineActive()) {
            if (typeof showToast === 'function') {
                showToast(getLocalizedLabel('QuickEntry_DisabledJustMine'), 'error');
            }
            return;
        }

        if (isActive) {
            isActive = false;
            window.quickEntryActive = false;
            closeInput();
            detachListeners();
            dismissTooltip();
            localStorage.setItem(LS_KEY_ACTIVE, 'false');
        } else {
            isActive = true;
            window.quickEntryActive = true;
            allItems = []; // force reload on next open
            attachListeners();
            showFirstUseTooltip();
            localStorage.setItem(LS_KEY_ACTIVE, 'true');
        }

        // Update button styling
        var btn = document.getElementById('quickEntryToggle');
        if (btn) {
            if (isActive) {
                btn.classList.remove('btn-ghost');
                btn.classList.add('btn-primary');
            } else {
                btn.classList.remove('btn-primary');
                btn.classList.add('btn-ghost');
            }
        }
    }

    // --- Init from localStorage ---

    function initFromStorage() {
        var saved = localStorage.getItem(LS_KEY_ACTIVE);
        if (saved === 'true') {
            setTimeout(function () { toggle(); }, 100);
        }
    }

    // --- Public API ---

    window.CalendarQuickEntry = {
        toggle: toggle,
        isActive: function () { return isActive; }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initFromStorage);
    } else {
        initFromStorage();
    }
})();
