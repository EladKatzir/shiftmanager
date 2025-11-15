/**
 * Shift Swap - Easter Egg Match-3 Game
 * Triggered by Ctrl+Click on .brand element
 */

(function() {
    'use strict';

    // Game configuration
    const GRID_SIZE = 6;
    const ICONS = ['⏰', '📅', '🧹', '☕', '📦', '🔔'];
    const POINTS_PER_TILE = 10;
    const ANIMATION_DURATION = 300;

    // Game state
    let grid = [];
    let score = 0;
    let selectedTile = null;
    let isAnimating = false;
    let modalElement = null;

    /**
     * Initialize and open the game
     */
    function openGame() {
        // Don't open if already open
        if (modalElement) return;

        // Initialize game state
        score = 0;
        selectedTile = null;
        isAnimating = false;

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
     * Create modal HTML and inject into DOM
     */
    function createModal() {
        const modalHTML = `
            <div class="shift-swap-modal" id="shiftSwapModal">
                <div class="shift-swap-backdrop"></div>
                <div class="shift-swap-content">
                    <div class="shift-swap-header">
                        <h2 class="shift-swap-title">Shift Swap</h2>
                        <button class="shift-swap-close" aria-label="Close game">&times;</button>
                    </div>
                    <p class="shift-swap-subtitle">Swap adjacent icons to line up 3+ in a row</p>
                    <div class="shift-swap-score">
                        <span class="score-label">Score:</span>
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
     * Add all event listeners
     */
    function addEventListeners() {
        // Close button
        const closeBtn = modalElement.querySelector('.shift-swap-close');
        closeBtn.addEventListener('click', closeGame);

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
     * Handle keyboard events
     */
    function handleKeydown(e) {
        if (e.key === 'Escape') {
            closeGame();
        }
    }

    /**
     * Close and cleanup game
     */
    function closeGame() {
        if (!modalElement) return;

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
     * Initialize grid with no initial matches
     */
    function initializeGrid() {
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
    }

    /**
     * Handle grid click
     */
    function handleGridClick(e) {
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
     * Find all matches on the board
     */
    function findAllMatches() {
        const matches = new Set();

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
     * Process matches (clear, update score, apply gravity, refill)
     */
    async function processMatches(matches) {
        // Add points
        const points = matches.length * POINTS_PER_TILE;
        score += points;
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
        }
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

    console.log('Shift Swap game loaded successfully!');

})();
