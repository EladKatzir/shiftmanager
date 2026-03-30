/**
 * Client-side Date Formatting (B-010)
 * Formats dates according to the current locale
 * Supports English (en) and Hebrew (he-IL) locales
 */
(function() {
    'use strict';

    // Determine locale from document
    var docLang = document.documentElement.lang || 'en';
    var locale = docLang === 'he' ? 'he-IL' : 'en-US';
    var isRTL = document.dir === 'rtl' || docLang === 'he';

    /**
     * DateFormat namespace for all date formatting functions
     */
    window.DateFormat = {
        /**
         * Get the current locale
         * @returns {string} The current locale (e.g., 'en-US' or 'he-IL')
         */
        getLocale: function() {
            return locale;
        },

        /**
         * Check if current locale is RTL
         * @returns {boolean}
         */
        isRTL: function() {
            return isRTL;
        },

        /**
         * Format a date for display (long format)
         * English: "January 30, 2026"
         * Hebrew: "30 בינואר 2026"
         * @param {Date|string|number} date - Date to format
         * @returns {string} Formatted date string
         */
        longDate: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleDateString(locale, {
                year: 'numeric',
                month: 'long',
                day: 'numeric'
            });
        },

        /**
         * Format a date in short format
         * English: "1/30/2026"
         * Hebrew: "30/1/2026"
         * @param {Date|string|number} date - Date to format
         * @returns {string} Formatted date string
         */
        shortDate: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleDateString(locale, {
                year: 'numeric',
                month: 'numeric',
                day: 'numeric'
            });
        },

        /**
         * Format month and year for calendar headers
         * English: "January 2026"
         * Hebrew: "ינואר 2026"
         * @param {Date|string|number} date - Date to format
         * @returns {string} Formatted month/year string
         */
        monthYear: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleDateString(locale, {
                year: 'numeric',
                month: 'long'
            });
        },

        /**
         * Format time in 24-hour format (HH:mm)
         * @param {Date|string|number} date - Date/time to format
         * @returns {string} Formatted time string (e.g., "14:30")
         */
        time: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleTimeString(locale, {
                hour: '2-digit',
                minute: '2-digit',
                hour12: false
            });
        },

        /**
         * Format time with seconds (HH:mm:ss)
         * @param {Date|string|number} date - Date/time to format
         * @returns {string} Formatted time string (e.g., "14:30:45")
         */
        timeLong: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleTimeString(locale, {
                hour: '2-digit',
                minute: '2-digit',
                second: '2-digit',
                hour12: false
            });
        },

        /**
         * Format date and time together
         * @param {Date|string|number} date - Date/time to format
         * @returns {string} Formatted date and time string
         */
        dateTime: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return this.longDate(d) + ' ' + this.time(d);
        },

        /**
         * Get relative time string (e.g., "5 minutes ago")
         * Uses localized strings from window.AppLocalizer
         * @param {Date|string|number} date - Date to compare against now
         * @returns {string} Relative time string
         */
        relative: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            var now = new Date();
            var diffMs = now - d;

            // Future dates: fall back to friendly format instead of showing "Just now"
            if (diffMs < 0) {
                return this.friendly(date);
            }

            var diffSecs = Math.floor(diffMs / 1000);
            var diffMins = Math.floor(diffMs / 60000);
            var diffHours = Math.floor(diffMs / 3600000);
            var diffDays = Math.floor(diffMs / 86400000);
            var diffWeeks = Math.floor(diffDays / 7);

            var strings = window.AppLocalizer || {};

            // Just now (less than 60 seconds)
            if (diffSecs < 60) {
                return strings.DateTime_JustNow || 'Just now';
            }

            // Minutes ago (less than 60 minutes)
            if (diffMins < 60) {
                return (strings.DateTime_MinutesAgo || '{0} minutes ago').replace('{0}', diffMins);
            }

            // Hours ago (less than 24 hours)
            if (diffHours < 24) {
                return (strings.DateTime_HoursAgo || '{0} hours ago').replace('{0}', diffHours);
            }

            // Yesterday
            if (diffDays === 1) {
                var timeStr = this.time(d);
                return (strings.DateTime_Yesterday || 'Yesterday at {0}').replace('{0}', timeStr);
            }

            // Days ago (less than 7 days)
            if (diffDays < 7) {
                return (strings.DateTime_DaysAgo || '{0} days ago').replace('{0}', diffDays);
            }

            // Weeks ago (less than 30 days)
            if (diffDays < 30) {
                return (strings.DateTime_WeeksAgo || '{0} weeks ago').replace('{0}', diffWeeks);
            }

            // For older dates, return the formatted long date
            return this.longDate(date);
        },

        /**
         * Get friendly date (Today, Tomorrow, Yesterday, or formatted date)
         * @param {Date|string|number} date - Date to format
         * @returns {string} Friendly date string
         */
        friendly: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            var today = new Date();
            today.setHours(0, 0, 0, 0);

            var dateOnly = new Date(d);
            dateOnly.setHours(0, 0, 0, 0);

            var diffDays = Math.round((dateOnly - today) / 86400000);
            var strings = window.AppLocalizer || {};

            if (diffDays === 0) {
                return strings.DateTime_Today || 'Today';
            }
            if (diffDays === 1) {
                return strings.DateTime_Tomorrow || 'Tomorrow';
            }
            if (diffDays === -1) {
                return (strings.DateTime_Yesterday || 'Yesterday at {0}').replace(' at {0}', '').replace(' ב-{0}', '');
            }

            return this.longDate(date);
        },

        /**
         * Get day name for a date
         * @param {Date|string|number} date - Date to get day name for
         * @returns {string} Full day name (e.g., "Monday" / "יום שני")
         */
        dayName: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleDateString(locale, { weekday: 'long' });
        },

        /**
         * Get abbreviated day name
         * @param {Date|string|number} date - Date to get day name for
         * @returns {string} Short day name (e.g., "Mon" / "ב'")
         */
        shortDayName: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleDateString(locale, { weekday: 'short' });
        },

        /**
         * Get month name
         * @param {Date|string|number} date - Date to get month name for
         * @returns {string} Full month name
         */
        monthName: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleDateString(locale, { month: 'long' });
        },

        /**
         * Get abbreviated month name
         * @param {Date|string|number} date - Date to get month name for
         * @returns {string} Short month name
         */
        shortMonthName: function(date) {
            var d = new Date(date);
            if (isNaN(d.getTime())) return '';

            return d.toLocaleDateString(locale, { month: 'short' });
        },

        /**
         * Format a date range
         * @param {Date|string|number} start - Start date
         * @param {Date|string|number} end - End date
         * @returns {string} Formatted date range
         */
        dateRange: function(start, end) {
            var s = new Date(start);
            var e = new Date(end);
            if (isNaN(s.getTime()) || isNaN(e.getTime())) return '';

            if (s.getFullYear() === e.getFullYear() && s.getMonth() === e.getMonth()) {
                // Same month
                if (isRTL) {
                    return s.getDate() + '-' + e.getDate() + ' ' + this.monthName(s) + ' ' + s.getFullYear();
                }
                return this.monthName(s) + ' ' + s.getDate() + '-' + e.getDate() + ', ' + s.getFullYear();
            }

            return this.longDate(start) + ' - ' + this.longDate(end);
        },

        /**
         * Format a time range
         * @param {Date|string|number} start - Start time
         * @param {Date|string|number} end - End time
         * @returns {string} Formatted time range (e.g., "08:00 - 16:00")
         */
        timeRange: function(start, end) {
            return this.time(start) + ' - ' + this.time(end);
        },

        /**
         * Parse an ISO date string and format it
         * @param {string} isoString - ISO date string
         * @param {string} format - Format type ('long', 'short', 'time', 'relative', 'friendly')
         * @returns {string} Formatted string
         */
        parseAndFormat: function(isoString, format) {
            format = format || 'long';
            var d = new Date(isoString);
            if (isNaN(d.getTime())) return isoString;

            switch (format) {
                case 'long':
                case 'longDate':
                    return this.longDate(d);
                case 'short':
                case 'shortDate':
                    return this.shortDate(d);
                case 'time':
                    return this.time(d);
                case 'timeLong':
                    return this.timeLong(d);
                case 'dateTime':
                    return this.dateTime(d);
                case 'relative':
                    return this.relative(d);
                case 'friendly':
                    return this.friendly(d);
                case 'monthYear':
                    return this.monthYear(d);
                case 'dayName':
                    return this.dayName(d);
                case 'shortDayName':
                    return this.shortDayName(d);
                default:
                    return this.longDate(d);
            }
        }
    };

    /**
     * Auto-format elements with data-date-format attribute on DOM ready.
     * Only formats elements that explicitly opt-in with data-date-format.
     * Usage: <span data-date="2026-01-30" data-date-format="longDate"></span>
     * Or: <span data-relative-time="2026-01-30T14:30:00Z"></span>
     */
    function autoFormatDates() {
        // Only format elements that explicitly have data-date-format attribute
        // (data-date alone is used as a DOM query attribute across many pages)
        var dateElements = document.querySelectorAll('[data-date][data-date-format]');
        for (var i = 0; i < dateElements.length; i++) {
            var el = dateElements[i];
            var date = el.getAttribute('data-date');
            var format = el.getAttribute('data-date-format');
            if (date && format && window.DateFormat[format]) {
                el.textContent = window.DateFormat[format](date);
            }
        }

        // Format elements with data-relative-time attribute
        var relativeElements = document.querySelectorAll('[data-relative-time]');
        for (var j = 0; j < relativeElements.length; j++) {
            var relEl = relativeElements[j];
            var relDate = relEl.getAttribute('data-relative-time');
            if (relDate) {
                relEl.textContent = window.DateFormat.relative(relDate);
            }
        }
    }

    // Run auto-format on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', autoFormatDates);
    } else {
        autoFormatDates();
    }

    // Also run after HTMX swaps (if HTMX is present)
    document.addEventListener('htmx:afterSwap', autoFormatDates);

    // Expose autoFormatDates for manual calls
    window.DateFormat.autoFormat = autoFormatDates;

})();
