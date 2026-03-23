/**
 * Shift Swap - Easter Egg Match-3 Game
 * ✅ PHASE 19: Enhanced with leaderboard, roasting messages, and score persistence
 * Triggered by Ctrl+Click on .brand element
 */

(function() {
    'use strict';

    // Game configuration (defaults - will be loaded from API)
    let GRID_SIZE = 6;
    const ICONS = ['⏰', '📅', '🧹', '☕', '📦', '🔔'];
    const POINTS_PER_TILE = 10;
    const BASE_ANIMATION_DURATION = 300;

    /**
     * Get animation duration for the game.
     * Always returns full duration — the game is an opt-in easter egg, so CSS
     * animations are exempted from the blanket reduced-motion rule in tokens.css.
     * The JS sleep() calls must match the CSS animation durations.
     */
    function getAnimationDuration() {
        return BASE_ANIMATION_DURATION;
    }

    // Dynamic animation duration (will be updated based on preference)
    let ANIMATION_DURATION = BASE_ANIMATION_DURATION;

    // ✅ Dynamic configuration loaded from API
    let gameConfig = null;
    let POINTS_3_MATCH = 40;
    let POINTS_4_MATCH = 100;
    let POINTS_5_PLUS_MATCH = 200;
    let MEGA_COMBO_MULTIPLIER = 2;
    let MEGA_COMBO_MIN_3_LINES = 0;
    let MEGA_COMBO_MIN_4_LINES = 2;
    let MEGA_COMBO_MIN_5_LINES = 0;
    let MILESTONES = [1000, 2500, 5000, 7500, 10000, 15000, 20000];

    // Game state
    let grid = [];
    let score = 0;
    let selectedTile = null;
    let isAnimating = false;
    let modalElement = null;
    let gameOver = false;  // Track if game has ended due to no moves

    // ✅ PHASE 19: Localization and milestone tracking
    let localization = null;
    let crossedMilestones = new Set();
    let highestMilestone = 0;

    /**
     * Get current culture from HTML lang attribute
     */
    function getCurrentCulture() {
        const htmlLang = document.documentElement.lang || 'en-US';
        return htmlLang.startsWith('he') ? 'he-IL' : 'en-US';
    }

    /**
     * ✅ PHASE 19: Load localization strings from API
     */
    async function loadLocalization() {
        try {
            const response = await fetch('/Api/Game/GetLocalization', {
                credentials: 'same-origin'
            });
            if (response.ok) {
                localization = await response.json();
                return true;
            }
        } catch (error) {
            console.error('Failed to load game localization:', error);
        }

        // Fallback to English defaults
        localization = {
            title: 'Shift Swap',
            instructions: 'Swap adjacent icons to line up 3+ in a row',
            score: 'Score:',
            trophy: 'Leaderboard',
            hint: 'Get hint',
            playAgain: 'Play Again',
            viewLeaderboard: 'View Leaderboard',
            scoreSaved: 'Score saved!',
            milestoneReached: 'New milestone reached! 🎉',
            noMovesLeft: 'No more moves available!',
            gameOver: 'Game Over',
            roasts: {
                r1000: ['What? Is there no work?', 'Should we assign you more shifts?', 'Is the real shift schedule not challenging enough?'],
                r2500: ['I know your next chore: practicing the game!', 'Your dedication is... concerning.', 'Is this why the shifts are piling up?'],
                r5000: ['The CEO of Shift Swapping.', 'the boss is watching... just kidding, keep going!', 'Shift Swapping Level: Expert. Productivity Level: We need to talk.'],
                r7500: ['Someone\'s trying to get a promotion through gaming.', 'The servers are now 10% your saved shifts.', 'We\'re adding this to your performance review.'],
                r10000: ['Ive low-key ran out of jokes, or maybe not!', 'You\'ve officially swapped more virtual shifts than real ones.', 'The ultimate power is not controlling time, but controlling the schedule.'],
                r15000: ['Okay, we might need to add you to the on-duty schedule... inside the game.', 'You\'ve achieved a state of perpetual shift-ness.', 'At this point, you\'re not playing a game, you\'re conducting an orchestra of shifts.'],
                r20000: ['We\'ve run out of jokes. Please go touch grass.', 'You have transcended. The game now plays you.', 'Congratulations! You have officially completed all the work. You may now retire.']
            },
            leaderboard: {
                title: 'Leaderboard',
                allTime: 'All Time',
                monthly: 'This Month',
                rank: 'Rank',
                player: 'Player',
                yourBest: 'Your Best',
                noScoresYet: 'No scores yet',
                backToGame: 'Back to Game',
                you: 'You'
            }
        };
        return true;
    }

    /**
     * Load game configuration from API
     */
    async function loadGameConfiguration() {
        try {
            const response = await fetch('/Api/Game/GetConfiguration', {
                credentials: 'same-origin'
            });
            if (response.ok) {
                gameConfig = await response.json();

                // Check if game is enabled
                if (gameConfig.enabled === false) {
                    console.log('Game is disabled by configuration');
                    return false;
                }

                // Apply configuration
                GRID_SIZE = gameConfig.gridSize || 6;
                POINTS_3_MATCH = gameConfig.scoring?.points3Match || 40;
                POINTS_4_MATCH = gameConfig.scoring?.points4Match || 100;
                POINTS_5_PLUS_MATCH = gameConfig.scoring?.points5PlusMatch || 200;
                MEGA_COMBO_MULTIPLIER = gameConfig.megaCombo?.multiplier || 2;
                MEGA_COMBO_MIN_3_LINES = gameConfig.megaCombo?.min3MatchLines || 0;
                MEGA_COMBO_MIN_4_LINES = gameConfig.megaCombo?.min4MatchLines || 2;
                MEGA_COMBO_MIN_5_LINES = gameConfig.megaCombo?.min5MatchLines || 0;
                MILESTONES = gameConfig.milestones || [1000, 2500, 5000, 7500, 10000, 15000, 20000];

                // Validate grid size
                if (GRID_SIZE < 4 || GRID_SIZE > 10) {
                    console.error('Invalid grid size:', GRID_SIZE, '- falling back to 6');
                    GRID_SIZE = 6;
                }

                console.log('Game configuration loaded:', gameConfig);
                return true;
            }
        } catch (error) {
            console.error('Failed to load game configuration:', error);
        }

        // Use defaults if config fails to load
        console.log('Using default game configuration');
        return true;
    }

    /**
     * ✅ PHASE 19: Initialize and open the game (with async localization loading)
     * B-001-EXT: Respects prefers-reduced-motion preference
     */
    async function openGame() {
        // Don't open if already open
        if (modalElement) return;

        ANIMATION_DURATION = getAnimationDuration();
        // Animation duration configured

        // Load configuration and localization
        const configLoaded = await loadGameConfiguration();
        if (!configLoaded) {
            alert(window.AppLocalizer?.Game_CurrentlyDisabled || 'The game is currently disabled.');
            return;
        }

        await loadLocalization();

        // Initialize game state
        score = 0;
        selectedTile = null;
        isAnimating = false;
        gameOver = false;
        crossedMilestones = new Set();
        highestMilestone = 0;

        // Create and inject modal
        createModal();

        // Initialize grid
        initializeGrid();
        renderGrid();

        // Show modal with animation
        setTimeout(() => {
            modalElement.classList.add('show');
        }, 10);
    }

    /**
     * ✅ PHASE 19: Create modal HTML with trophy button
     */
    function createModal() {
        const modalHTML = `
            <div class="shift-swap-modal" id="shiftSwapModal">
                <div class="shift-swap-backdrop"></div>
                <div class="shift-swap-content">
                    <div class="shift-swap-header">
                        <h2 class="shift-swap-title">${localization.title}</h2>
                        <div class="shift-swap-header-actions">
                            <button class="shift-swap-hint" aria-label="${localization.hint}" title="${localization.hint}">💡</button>
                            <button class="shift-swap-trophy" aria-label="${localization.trophy}" title="${localization.trophy}">🏆</button>
                            <button class="shift-swap-close" aria-label="Close game">&times;</button>
                        </div>
                    </div>
                    <p class="shift-swap-subtitle">${localization.instructions}</p>
                    <div class="shift-swap-score">
                        <span class="score-label">${localization.score}</span>
                        <span class="score-value" id="shiftSwapScore">0</span>
                    </div>
                    <div class="shift-swap-grid" id="shiftSwapGrid"></div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHTML);
        modalElement = document.getElementById('shiftSwapModal');

        // Add event listeners
        addEventListeners();
    }

    /**
     * ✅ PHASE 19: Add event listeners (includes trophy button)
     */
    function addEventListeners() {
        // Close button
        const closeBtn = modalElement.querySelector('.shift-swap-close');
        closeBtn.addEventListener('click', closeGame);

        // Hint button
        const hintBtn = modalElement.querySelector('.shift-swap-hint');
        hintBtn.addEventListener('click', showHint);

        // ✅ PHASE 19: Trophy button
        const trophyBtn = modalElement.querySelector('.shift-swap-trophy');
        trophyBtn.addEventListener('click', openLeaderboard);

        // Backdrop click
        const backdrop = modalElement.querySelector('.shift-swap-backdrop');
        backdrop.addEventListener('click', closeGame);

        // Prevent clicks inside modal from closing
        const content = modalElement.querySelector('.shift-swap-content');
        content.addEventListener('click', (e) => e.stopPropagation());

        // Keyboard (ESC)
        document.addEventListener('keydown', handleKeydown);

        // Grid clicks
        const gridElement = document.getElementById('shiftSwapGrid');
        gridElement.addEventListener('click', handleGridClick);
    }

    /**
     * ✅ PHASE 19: Open leaderboard page
     */
    function openLeaderboard() {
        window.open('/Game/Leaderboard', '_blank');
    }

    /**
     * Handle keyboard events
     */
    function handleKeydown(e) {
        if (e.key === 'Escape') {
            closeGame();
        }
    }

    /**
     * ✅ PHASE 19: Close and cleanup game (with roasting popup)
     */
    async function closeGame() {
        if (!modalElement) return;

        // ✅ PHASE 19: Show roasting popup if score > 0
        if (score > 0) {
            await showRoastingPopup();
        }

        // Remove show class for exit animation
        modalElement.classList.remove('show');

        // Wait for animation then remove from DOM
        setTimeout(() => {
            if (modalElement && modalElement.parentNode) {
                // Remove event listeners
                document.removeEventListener('keydown', handleKeydown);

                // Remove modal from DOM
                modalElement.remove();
                modalElement = null;
            }
        }, ANIMATION_DURATION);
    }

    /**
     * ✅ PHASE 19: Show roasting popup with score and random message
     */
    function showRoastingPopup() {
        return new Promise((resolve) => {
            // Determine highest milestone and get random variant
            const milestone = highestMilestone;
            const roastMessage = milestone > 0 ? getRandomRoast(milestone) : null;

            if (!roastMessage) {
                resolve();
                return;
            }

            // Create popup HTML
            const popupHTML = `
                <div class="roasting-popup" id="roastingPopup">
                    <div class="roasting-popup-backdrop"></div>
                    <div class="roasting-popup-content">
                        <div class="roasting-trophy-icon">🏆</div>
                        <h3 class="roasting-score-display">${localization.score} ${score}</h3>
                        <p class="roasting-message">${roastMessage}</p>
                        <div class="roasting-buttons">
                            <button class="btn-modal primary" id="playAgainBtn">${localization.playAgain}</button>
                            <button class="btn-modal secondary" id="viewLeaderboardBtn">${localization.viewLeaderboard}</button>
                        </div>
                    </div>
                </div>
            `;

            document.body.insertAdjacentHTML('beforeend', popupHTML);
            const popup = document.getElementById('roastingPopup');

            // Show popup with animation
            setTimeout(() => popup.classList.add('show'), 10);

            // Add button handlers
            document.getElementById('playAgainBtn').addEventListener('click', () => {
                closePopup(popup);
                saveScore();
                resolve();
                // Reopen game
                setTimeout(() => openGame(), 400);
            });

            document.getElementById('viewLeaderboardBtn').addEventListener('click', () => {
                closePopup(popup);
                saveScore();
                openLeaderboard();
                resolve();
            });

            // Backdrop click
            popup.querySelector('.roasting-popup-backdrop').addEventListener('click', () => {
                closePopup(popup);
                saveScore();
                resolve();
            });
        });
    }

    /**
     * ✅ PHASE 19: Get random roast message for milestone
     */
    function getRandomRoast(milestone) {
        // Defensive null checks to prevent TypeError
        if (!localization || !localization.roasts) {
            console.error('Localization data not loaded for roasting system');
            return null;
        }

        const roastKey = `r${milestone}`;
        const variants = localization.roasts[roastKey];
        if (!variants || variants.length === 0) return null;

        const randomIndex = Math.floor(Math.random() * variants.length);
        return variants[randomIndex];
    }

    /**
     * ✅ PHASE 19: Close roasting popup
     */
    function closePopup(popup) {
        popup.classList.remove('show');
        setTimeout(() => {
            if (popup && popup.parentNode) {
                popup.remove();
            }
        }, ANIMATION_DURATION);
    }

    /**
     * ✅ PHASE 19: Save score to backend
     */
    async function saveScore() {
        if (score === 0) return;

        try {
            const response = await fetch('/Api/Game/SaveScore', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                credentials: 'same-origin',
                body: JSON.stringify({ score: score })
            });

            if (response.ok) {
                const result = await response.json();
                showToast(localization.scoreSaved, 'success');
                console.log('Score saved! Rank:', result.rank);
            }
        } catch (error) {
            console.error('Failed to save score:', error);
        }
    }

    /**
     * ✅ PHASE 19: Show toast notification
     * B-001-EXT: Respects prefers-reduced-motion preference
     */
    function showToast(message, type = 'info') {
        const toast = document.createElement('div');
        toast.className = `toast toast--${type}`;
        toast.textContent = message;
        document.body.appendChild(toast);

        const animationDelay = 10;
        const animationTime = 300;

        setTimeout(() => toast.classList.add('show'), animationDelay);

        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), animationTime);
        }, 3000);
    }

    /**
     * Initialize grid with no initial matches
     */
    function initializeGrid() {
        // Defensive validation
        if (!GRID_SIZE || GRID_SIZE < 4 || GRID_SIZE > 10) {
            console.error('Invalid GRID_SIZE:', GRID_SIZE, '- using default 6');
            GRID_SIZE = 6;
        }

        grid = [];

        // Create random grid
        for (let row = 0; row < GRID_SIZE; row++) {
            grid[row] = [];
            for (let col = 0; col < GRID_SIZE; col++) {
                grid[row][col] = getRandomIcon();
            }
        }

        // Remove any initial matches
        let hasMatches = true;
        let attempts = 0;
        while (hasMatches && attempts < 100) {
            hasMatches = false;
            for (let row = 0; row < GRID_SIZE; row++) {
                for (let col = 0; col < GRID_SIZE; col++) {
                    if (isPartOfMatch(row, col)) {
                        grid[row][col] = getRandomIcon();
                        hasMatches = true;
                    }
                }
            }
            attempts++;
        }
    }

    /**
     * Get random icon
     */
    function getRandomIcon() {
        return ICONS[Math.floor(Math.random() * ICONS.length)];
    }

    /**
     * Check if a tile is part of a match
     */
    function isPartOfMatch(row, col) {
        const icon = grid[row][col];

        // Check horizontal
        let horizontalCount = 1;
        // Check left
        for (let c = col - 1; c >= 0 && grid[row][c] === icon; c--) {
            horizontalCount++;
        }
        // Check right
        for (let c = col + 1; c < GRID_SIZE && grid[row][c] === icon; c++) {
            horizontalCount++;
        }

        if (horizontalCount >= 3) return true;

        // Check vertical
        let verticalCount = 1;
        // Check up
        for (let r = row - 1; r >= 0 && grid[r][col] === icon; r--) {
            verticalCount++;
        }
        // Check down
        for (let r = row + 1; r < GRID_SIZE && grid[r][col] === icon; r++) {
            verticalCount++;
        }

        return verticalCount >= 3;
    }

    /**
     * Render the grid
     */
    function renderGrid() {
        const gridElement = document.getElementById('shiftSwapGrid');
        if (!gridElement) return;

        // Apply dynamic grid size
        gridElement.style.gridTemplateColumns = `repeat(${GRID_SIZE}, 1fr)`;

        gridElement.innerHTML = '';

        for (let row = 0; row < GRID_SIZE; row++) {
            for (let col = 0; col < GRID_SIZE; col++) {
                const tile = document.createElement('div');
                tile.className = 'shift-swap-tile';
                tile.dataset.row = row;
                tile.dataset.col = col;
                tile.textContent = grid[row][col];

                // Highlight selected tile
                if (selectedTile && selectedTile.row === row && selectedTile.col === col) {
                    tile.classList.add('selected');
                }

                gridElement.appendChild(tile);
            }
        }

        // Update hint button state
        updateHintButton();
    }

    /**
     * Update hint button enabled/disabled state
     */
    function updateHintButton() {
        const hintBtn = modalElement?.querySelector('.shift-swap-hint');
        if (!hintBtn) return;

        const possibleMoves = getPossibleMerges();
        hintBtn.disabled = possibleMoves.length === 0 || isAnimating;
    }

    /**
     * Handle grid click
     */
    function handleGridClick(e) {
        clearHints();  // Clear any active hints when user clicks

        if (isAnimating) return;

        const tile = e.target.closest('.shift-swap-tile');
        if (!tile) return;

        const row = parseInt(tile.dataset.row);
        const col = parseInt(tile.dataset.col);

        if (!selectedTile) {
            // First selection
            selectedTile = { row, col };
            renderGrid();
        } else {
            // Second selection - check if adjacent
            if (isAdjacent(selectedTile, { row, col })) {
                // Attempt swap
                attemptSwap(selectedTile, { row, col });
            } else {
                // Not adjacent, select new tile
                selectedTile = { row, col };
                renderGrid();
            }
        }
    }

    /**
     * Check if two tiles are adjacent
     */
    function isAdjacent(tile1, tile2) {
        const rowDiff = Math.abs(tile1.row - tile2.row);
        const colDiff = Math.abs(tile1.col - tile2.col);
        return (rowDiff === 1 && colDiff === 0) || (rowDiff === 0 && colDiff === 1);
    }

    /**
     * Attempt to swap two tiles
     */
    async function attemptSwap(tile1, tile2) {
        isAnimating = true;

        // Swap tiles
        const temp = grid[tile1.row][tile1.col];
        grid[tile1.row][tile1.col] = grid[tile2.row][tile2.col];
        grid[tile2.row][tile2.col] = temp;

        renderGrid();
        await sleep(ANIMATION_DURATION / 2);

        // Check for matches
        const matches = findAllMatches();

        if (matches.length > 0) {
            // Valid swap - clear matches and continue
            selectedTile = null;
            await processMatches(matches);
        } else {
            // Invalid swap - swap back
            grid[tile2.row][tile2.col] = grid[tile1.row][tile1.col];
            grid[tile1.row][tile1.col] = temp;
            renderGrid();
            await sleep(ANIMATION_DURATION / 2);
            selectedTile = null;
        }

        isAnimating = false;
        renderGrid();
    }

    /**
     * ✅ SUPER-MERGE: Find all matches on the board (with super-merge detection)
     */
    function findAllMatches() {
        const findStart = performance.now();
        const matches = new Set();
        const superMergeLines = []; // Track 4-in-a-row lines

        // Check horizontal matches
        for (let row = 0; row < GRID_SIZE; row++) {
            for (let col = 0; col < GRID_SIZE - 2; col++) {
                const icon = grid[row][col];
                if (icon === grid[row][col + 1] && icon === grid[row][col + 2]) {
                    // Found match, add all consecutive tiles
                    let endCol = col + 2;
                    while (endCol < GRID_SIZE && grid[row][endCol] === icon) {
                        endCol++;
                    }

                    const lineLength = endCol - col;

                    // ✅ SUPER-MERGE: Track 4-in-a-row lines
                    if (lineLength >= 4) {
                        superMergeLines.push({
                            type: 'horizontal',
                            row: row,
                            startCol: col,
                            endCol: endCol,
                            icon: icon
                        });
                    }

                    for (let c = col; c < endCol; c++) {
                        matches.add(`${row},${c}`);
                    }
                }
            }
        }

        // Check vertical matches
        for (let col = 0; col < GRID_SIZE; col++) {
            for (let row = 0; row < GRID_SIZE - 2; row++) {
                const icon = grid[row][col];
                if (icon === grid[row + 1][col] && icon === grid[row + 2][col]) {
                    // Found match, add all consecutive tiles
                    let endRow = row + 2;
                    while (endRow < GRID_SIZE && grid[endRow][col] === icon) {
                        endRow++;
                    }

                    const lineLength = endRow - row;

                    // ✅ SUPER-MERGE: Track 4-in-a-row lines
                    if (lineLength >= 4) {
                        superMergeLines.push({
                            type: 'vertical',
                            col: col,
                            startRow: row,
                            endRow: endRow,
                            icon: icon
                        });
                    }

                    for (let r = row; r < endRow; r++) {
                        matches.add(`${r},${col}`);
                    }
                }
            }
        }

        const matchArray = Array.from(matches).map(coord => {
            const [row, col] = coord.split(',').map(Number);
            return { row, col };
        });

        // ✅ SUPER-MERGE: Attach super-merge lines to result
        matchArray.superMergeLines = superMergeLines;

        return matchArray;
    }

    /**
     * Calculate the length of a super-merge line
     * @param {Object} line - Line object with type and coordinates
     * @returns {number} Length of the line
     */
    function getLineLength(line) {
        if (line.type === 'horizontal') {
            return line.endCol - line.startCol;
        } else if (line.type === 'vertical') {
            return line.endRow - line.startRow;
        }
        return 0;
    }

    /**
     * Find matches in a specific grid (modified version of findAllMatches)
     * @param {Array} targetGrid - 2D array to check for matches
     * @returns {Array} Array of matched tile coordinates
     */
    function findMatchesInGrid(targetGrid) {
        const matches = new Set();
        const gridSize = targetGrid.length;

        // Check horizontal matches
        for (let row = 0; row < gridSize; row++) {
            for (let col = 0; col < gridSize - 2; col++) {
                const icon = targetGrid[row][col];
                if (icon === targetGrid[row][col + 1] && icon === targetGrid[row][col + 2]) {
                    let endCol = col + 2;
                    while (endCol < gridSize && targetGrid[row][endCol] === icon) {
                        endCol++;
                    }
                    for (let c = col; c < endCol; c++) {
                        matches.add(`${row},${c}`);
                    }
                }
            }
        }

        // Check vertical matches
        for (let col = 0; col < gridSize; col++) {
            for (let row = 0; row < gridSize - 2; row++) {
                const icon = targetGrid[row][col];
                if (icon === targetGrid[row + 1][col] && icon === targetGrid[row + 2][col]) {
                    let endRow = row + 2;
                    while (endRow < gridSize && targetGrid[endRow][col] === icon) {
                        endRow++;
                    }
                    for (let r = row; r < endRow; r++) {
                        matches.add(`${r},${col}`);
                    }
                }
            }
        }

        return Array.from(matches).map(coord => {
            const [row, col] = coord.split(',').map(Number);
            return { row, col };
        });
    }

    /**
     * Find all possible valid moves on the current board
     * @param {boolean} findAll - If false, returns after finding first match (for performance)
     * @returns {Array} Array of possible moves with tile positions and matches
     */
    function getPossibleMerges(findAll = true) {
        const start = performance.now();
        const possibleMoves = [];

        for (let row = 0; row < GRID_SIZE; row++) {
            for (let col = 0; col < GRID_SIZE; col++) {
                // Check all 4 adjacent positions
                const adjacentPositions = [
                    { row: row, col: col + 1 },     // Right
                    { row: row + 1, col: col },     // Down
                ];

                // Only check right and down to avoid duplicate checks
                for (const adjacent of adjacentPositions) {
                    if (adjacent.row >= GRID_SIZE || adjacent.col >= GRID_SIZE) {
                        continue;
                    }

                    // Simulate swap without modifying actual grid
                    const tempGrid = JSON.parse(JSON.stringify(grid));
                    const temp = tempGrid[row][col];
                    tempGrid[row][col] = tempGrid[adjacent.row][adjacent.col];
                    tempGrid[adjacent.row][adjacent.col] = temp;

                    // Check if swap creates matches
                    const matches = findMatchesInGrid(tempGrid);

                    if (matches.length > 0) {
                        possibleMoves.push({
                            tile1: { row, col },
                            tile2: { row: adjacent.row, col: adjacent.col },
                            matches: matches
                        });

                        // Early exit if we just need to know if ANY move exists
                        if (!findAll) {
                            return possibleMoves;
                        }
                    }
                }
            }
        }

        return possibleMoves;
    }

    /**
     * Show hint by highlighting a possible merge
     */
    function showHint() {
        if (isAnimating) return;

        // Clear any existing hints
        clearHints();

        const possibleMoves = getPossibleMerges();

        if (possibleMoves.length === 0) {
            // No hints available - should trigger lose condition
            console.warn('No possible moves available');
            return;
        }

        // Pick first available move (or random: Math.floor(Math.random() * possibleMoves.length))
        const hint = possibleMoves[0];

        // Highlight the first 3 matching tiles
        const tilesToHighlight = [hint.tile1, hint.tile2];

        // Add one more tile from the matches to show which 3 tiles will merge
        if (hint.matches.length > 0) {
            tilesToHighlight.push(hint.matches[0]);
        }

        // Apply hint highlight to tiles
        tilesToHighlight.forEach(({ row, col }) => {
            const tile = document.querySelector(`[data-row="${row}"][data-col="${col}"]`);
            if (tile) {
                tile.classList.add('hint-highlight');
            }
        });

        // Auto-clear hint after 3 seconds
        setTimeout(clearHints, 3000);
    }

    /**
     * Clear all hint highlights
     */
    function clearHints() {
        document.querySelectorAll('.hint-highlight').forEach(tile => {
            tile.classList.remove('hint-highlight');
        });
    }

    /**
     * Check if player has lost (no possible moves)
     */
    async function checkForLoseCondition() {
        if (gameOver) return;

        const possibleMoves = getPossibleMerges(false); // Early exit for performance

        if (possibleMoves.length === 0) {
            gameOver = true;

            // Wait a moment for player to see the board
            await sleep(500);

            // Trigger game over with special message
            await triggerGameOver();
        }
    }

    /**
     * Trigger game over sequence
     */
    async function triggerGameOver() {
        if (!modalElement) return;

        // Show game over popup (modified roasting popup)
        await showGameOverPopup();

        // Close the game modal
        modalElement.classList.remove('show');

        setTimeout(() => {
            if (modalElement && modalElement.parentNode) {
                document.removeEventListener('keydown', handleKeydown);
                modalElement.remove();
                modalElement = null;
            }
        }, ANIMATION_DURATION);
    }

    /**
     * Show game over popup with score and no-moves message
     */
    function showGameOverPopup() {
        return new Promise((resolve) => {
            const milestone = highestMilestone;
            const roastMessage = milestone > 0 ? getRandomRoast(milestone) : null;

            // Custom game over message
            const gameOverMessage = localization.noMovesLeft || 'No more moves available!';

            const popupHTML = `
                <div class="roasting-popup game-over-popup" id="gameOverPopup">
                    <div class="roasting-popup-backdrop"></div>
                    <div class="roasting-popup-content">
                        <div class="game-over-icon">😵</div>
                        <h3 class="game-over-title">${localization.gameOver || 'Game Over'}</h3>
                        <p class="game-over-reason">${gameOverMessage}</p>
                        <h3 class="roasting-score-display">${localization.score} ${score}</h3>
                        ${roastMessage ? `<p class="roasting-message">${roastMessage}</p>` : ''}
                        <div class="roasting-buttons">
                            <button class="btn-modal primary" id="playAgainBtn">${localization.playAgain}</button>
                            <button class="btn-modal secondary" id="viewLeaderboardBtn">${localization.viewLeaderboard}</button>
                        </div>
                    </div>
                </div>
            `;

            document.body.insertAdjacentHTML('beforeend', popupHTML);
            const popup = document.getElementById('gameOverPopup');

            setTimeout(() => popup.classList.add('show'), 10);

            // Button handlers
            document.getElementById('playAgainBtn').addEventListener('click', () => {
                closePopup(popup);
                saveScore();
                resolve();
                setTimeout(() => openGame(), 400);
            });

            document.getElementById('viewLeaderboardBtn').addEventListener('click', () => {
                closePopup(popup);
                saveScore();
                openLeaderboard();
                resolve();
            });

            popup.querySelector('.roasting-popup-backdrop').addEventListener('click', () => {
                closePopup(popup);
                saveScore();
                resolve();
            });
        });
    }

    /**
     * ✅ SUPER-MERGE: Process matches (with super-merge combo detection)
     */
    async function processMatches(matches) {
        // Check for mega combo (2+ lines of 4)
        const superMergeLines = matches.superMergeLines || [];

        const isMegaCombo = superMergeLines.length >= 2;

        if (isMegaCombo) {
            // MEGA COMBO: Try to trigger mega combo (returns true if processed)
            const megaComboProcessed = await handleMegaCombo(superMergeLines);

            if (megaComboProcessed) {
                return; // Exit early after mega combo
            }
            // Fall through to handle as individual super-merges
        }

        if (superMergeLines.length === 1) {
            // SUPER-MERGE: Single 4-in-a-row combo
            await handleSuperMerge(superMergeLines[0], matches);
            return; // Exit early after super-merge
        } else if (superMergeLines.length >= 2) {
            // Process the longest line first
            const sortedLines = superMergeLines.sort((a, b) => getLineLength(b) - getLineLength(a));
            await handleSuperMerge(sortedLines[0], matches);
            return; // Exit early (cascades will handle remaining matches)
        }

        // Regular match processing (3-in-a-row)
        const points = POINTS_3_MATCH; // Configured points for 3-match
        score += points;

        // ✅ PHASE 19: Check for milestone crossing
        checkMilestones();

        updateScore();

        // Mark tiles for clearing with animation
        matches.forEach(({ row, col }) => {
            const tile = document.querySelector(`[data-row="${row}"][data-col="${col}"]`);
            if (tile) {
                tile.classList.add('clearing');
            }
        });

        await sleep(ANIMATION_DURATION);

        // Clear matched tiles
        matches.forEach(({ row, col }) => {
            grid[row][col] = null;
        });

        // Apply gravity
        applyGravity();
        renderGrid();
        await sleep(ANIMATION_DURATION);

        // Refill empty spaces
        refillGrid();
        renderGrid();
        await sleep(ANIMATION_DURATION);

        // Check for new matches (cascade)
        const newMatches = findAllMatches();
        if (newMatches.length > 0) {
            await processMatches(newMatches);
        } else {
            // No more cascades - check if any moves left
            await checkForLoseCondition();
        }
    }

    /**
     * ✨ SUPER-MERGE: Handle single 4-in-a-row combo
     */
    async function handleSuperMerge(line, matches) {
        // Extract tiles that belong to the super-merge line
        let superMergeTiles = [];

        if (line.type === 'horizontal') {
            // Horizontal: same row, columns from startCol to endCol
            for (let c = line.startCol; c < line.endCol; c++) {
                superMergeTiles.push({ row: line.row, col: c });
            }
        } else {
            // Vertical: same column, rows from startRow to endRow
            for (let r = line.startRow; r < line.endRow; r++) {
                superMergeTiles.push({ row: r, col: line.col });
            }
        }

        const lineLength = superMergeTiles.length;
        let points = 0;
        let clearEntireRowCol = false;

        if (lineLength === 4) {
            // 4-in-a-row: configured points
            points = POINTS_4_MATCH;
        } else if (lineLength >= 5) {
            // 5+ in-a-row: configured points + clear entire row/column
            points = POINTS_5_PLUS_MATCH;
            clearEntireRowCol = true;
        }

        score += points;
        checkMilestones();
        updateScore();

        // Show super-merge notification
        showSuperMergeEffect();

        // Cache all tiles once, then iterate
        const allTiles = document.querySelectorAll('.shift-swap-tile');

        // Mark tiles for animation
        let markedCount = 0;

        if (clearEntireRowCol) {
            // Mark entire row or column for 5+
            if (line.type === 'horizontal') {
                // Find all tiles in the row
                allTiles.forEach(tile => {
                    if (parseInt(tile.dataset.row) === line.row) {
                        tile.classList.add('super-merge');
                        markedCount++;
                    }
                });
            } else {
                // Find all tiles in the column
                allTiles.forEach(tile => {
                    if (parseInt(tile.dataset.col) === line.col) {
                        tile.classList.add('super-merge');
                        markedCount++;
                    }
                });
            }
        } else {
            // Mark only matched tiles for 4-match
            allTiles.forEach(tile => {
                const tileRow = parseInt(tile.dataset.row);
                const tileCol = parseInt(tile.dataset.col);
                if (superMergeTiles.some(({ row, col }) => row === tileRow && col === tileCol)) {
                    tile.classList.add('super-merge');
                    markedCount++;
                }
            });
        }

        const animDuration = ANIMATION_DURATION * 1.5;
        await sleep(animDuration);

        // Clear tiles based on match length

        if (clearEntireRowCol) {
            // Clear entire row or column for 5+
            if (line.type === 'horizontal') {
                for (let c = 0; c < GRID_SIZE; c++) {
                    grid[line.row][c] = null;
                }
            } else {
                for (let r = 0; r < GRID_SIZE; r++) {
                    grid[r][line.col] = null;
                }
            }
        } else {
            // Clear only matched tiles for 4-match
            superMergeTiles.forEach(({ row, col }) => {
                grid[row][col] = null;
            });
        }

        // Apply gravity
        applyGravity();
        renderGrid();
        await sleep(ANIMATION_DURATION);

        // Refill empty spaces
        refillGrid();

        renderGrid();
        await sleep(ANIMATION_DURATION);

        // Check for new matches (cascade)
        const newMatches = findAllMatches();

        if (newMatches.length > 0) {
            await processMatches(newMatches);
        } else {
            // No more cascades - check if any moves left
            await checkForLoseCondition();
        }
    }

    /**
     * 🎆 MEGA COMBO: Clear entire board and apply multiplier!
     * @returns {boolean} True if mega combo was processed, false if threshold not met
     */
    async function handleMegaCombo(lines) {
        // Defensive check
        if (!lines || lines.length === 0) {
            return false;
        }

        // Count lines by size
        const count3Lines = lines.filter(line => getLineLength(line) === 3).length;
        const count4Lines = lines.filter(line => getLineLength(line) === 4).length;
        const count5Lines = lines.filter(line => getLineLength(line) >= 5).length;

        // Check if any threshold is met
        const megaComboTriggered =
            (MEGA_COMBO_MIN_3_LINES > 0 && count3Lines >= MEGA_COMBO_MIN_3_LINES) ||
            (MEGA_COMBO_MIN_4_LINES > 0 && count4Lines >= MEGA_COMBO_MIN_4_LINES) ||
            (MEGA_COMBO_MIN_5_LINES > 0 && count5Lines >= MEGA_COMBO_MIN_5_LINES);

        if (!megaComboTriggered) {
            return false; // Threshold not met - caller should handle as regular super-merge
        }

        // Apply configured multiplier to current score
        score = score * MEGA_COMBO_MULTIPLIER;

        checkMilestones();
        updateScore();

        // Show mega combo explosion effect
        showMegaComboEffect();

        // Query all tiles once, add class with CSS-based stagger
        const allTiles = document.querySelectorAll('.shift-swap-tile');

        allTiles.forEach((tile, index) => {
            tile.classList.add('mega-combo');
            // Use CSS animation-delay for stagger effect (no blocking!)
            tile.style.animationDelay = `${index * 20}ms`;
        });

        // Wait for wave effect to complete (36 tiles x 20ms) + base animation
        const waitDuration = ANIMATION_DURATION * 2 + (allTiles.length * 20);
        await sleep(waitDuration);

        // Clear entire grid
        for (let row = 0; row < GRID_SIZE; row++) {
            for (let col = 0; col < GRID_SIZE; col++) {
                grid[row][col] = null;
            }
        }

        // Clean up animation delays
        allTiles.forEach(tile => {
            tile.style.animationDelay = '';
        });

        renderGrid();
        await sleep(ANIMATION_DURATION);

        // Refill with completely new grid
        refillGrid();

        renderGrid();
        await sleep(ANIMATION_DURATION * 1.5);

        // Check for new matches (cascade)
        const newMatches = findAllMatches();

        if (newMatches.length > 0) {
            await processMatches(newMatches);
        } else {
            // No more cascades - check if any moves left
            await checkForLoseCondition();
        }

        return true; // Successfully processed mega combo
    }

    /**
     * ✨ Show super-merge visual effect
     */
    function showSuperMergeEffect() {
        const culture = getCurrentCulture();
        const message = culture === 'he-IL' ? '✨ סופר מיזוג! ✨' : '✨ SUPER MERGE! ✨';
        showFloatingText(message, 'super-merge-text');
    }

    /**
     * 🎆 Show mega combo visual effect
     */
    function showMegaComboEffect() {
        const culture = getCurrentCulture();
        const message = culture === 'he-IL' ? '🎆 מגה קומבו! ניקוי מלא! 🎆' : '🎆 MEGA COMBO! BOARD CLEAR! 🎆';
        showFloatingText(message, 'mega-combo-text');
    }

    /**
     * Show floating text effect
     */
    function showFloatingText(message, className) {
        const floatingText = document.createElement('div');
        floatingText.className = `floating-combo-text ${className}`;
        floatingText.textContent = message;

        modalElement.querySelector('.shift-swap-content').appendChild(floatingText);

        setTimeout(() => {
            floatingText.classList.add('show');
        }, 10);

        setTimeout(() => {
            floatingText.classList.remove('show');
            setTimeout(() => floatingText.remove(), 500);
        }, 2500);
    }

    /**
     * ✅ PHASE 19: Check for milestone crossings and show popup
     */
    function checkMilestones() {
        for (const milestone of MILESTONES) {
            if (score >= milestone && !crossedMilestones.has(milestone)) {
                crossedMilestones.add(milestone);
                highestMilestone = Math.max(highestMilestone, milestone);

                // Show milestone popup with roasting message
                showMilestonePopup(milestone);
                break; // Only show one popup at a time
            }
        }
    }

    /**
     * ✅ Show milestone reached popup with roasting message
     */
    function showMilestonePopup(milestone) {
        const roastMessage = getRandomRoast(milestone);

        if (!roastMessage) {
            return;
        }

        // Create popup HTML
        const popupHTML = `
            <div class="milestone-popup" style="
                position: fixed;
                top: 50%;
                left: 50%;
                transform: translate(-50%, -50%);
                background: var(--surface, #fff);
                border: 2px solid #fbbf24;
                border-radius: 12px;
                padding: 2rem;
                box-shadow: 0 10px 40px rgba(0,0,0,0.3);
                z-index: 10001;
                max-width: 400px;
                text-align: center;
                animation: popIn 0.3s ease-out;
            ">
                <div style="font-size: 3rem; margin-bottom: 1rem;">🏆</div>
                <h3 style="margin: 0 0 0.5rem 0; color: #fbbf24; font-size: 1.5rem;">
                    ${localization.milestoneReached || 'Milestone Reached!'}
                </h3>
                <p style="font-size: 1.25rem; font-weight: 600; margin: 0.5rem 0; color: var(--primary, #3b82f6);">
                    ${milestone} ${localization.score || 'points'}
                </p>
                <p style="margin: 1rem 0; color: var(--text-primary, #333); font-size: 1rem; font-style: italic;">
                    "${roastMessage}"
                </p>
                <button onclick="this.parentElement.remove()" style="
                    background: var(--primary, #3b82f6);
                    color: white;
                    border: none;
                    padding: 0.75rem 1.5rem;
                    border-radius: 8px;
                    font-size: 1rem;
                    cursor: pointer;
                    margin-top: 1rem;
                ">Continue</button>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', popupHTML);

        // Auto-close after 5 seconds
        const popup = document.body.lastElementChild;
        setTimeout(() => {
            if (popup && popup.parentElement) {
                popup.remove();
            }
        }, 5000);
    }

    /**
     * Apply gravity - make tiles fall down
     */
    function applyGravity() {
        for (let col = 0; col < GRID_SIZE; col++) {
            // Collect non-null tiles from bottom to top
            const tiles = [];
            for (let row = GRID_SIZE - 1; row >= 0; row--) {
                if (grid[row][col] !== null) {
                    tiles.push(grid[row][col]);
                }
            }

            // Refill column from bottom
            for (let row = GRID_SIZE - 1; row >= 0; row--) {
                if (tiles.length > 0) {
                    grid[row][col] = tiles.shift();
                } else {
                    grid[row][col] = null;
                }
            }
        }
    }

    /**
     * Refill empty spaces with new random tiles
     */
    function refillGrid() {
        for (let row = 0; row < GRID_SIZE; row++) {
            for (let col = 0; col < GRID_SIZE; col++) {
                if (grid[row][col] === null) {
                    grid[row][col] = getRandomIcon();
                }
            }
        }
    }

    /**
     * Update score display
     */
    function updateScore() {
        const scoreElement = document.getElementById('shiftSwapScore');
        if (scoreElement) {
            scoreElement.textContent = score;
        }
    }

    /**
     * Sleep helper
     */
    function sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    // Export to global scope
    window.ShiftSwapGame = {
        open: openGame,
        close: closeGame
    };

    // Game module loaded (debug logs removed for production)

})();
