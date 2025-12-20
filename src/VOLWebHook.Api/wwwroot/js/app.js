/**
 * ═══════════════════════════════════════════════════════════════════════════
 * VOLWebHook Enterprise UI - Main JavaScript Application
 * ═══════════════════════════════════════════════════════════════════════════
 */

// ─────────────────────────────────────────────────────────────────────────────
// Application State
// ─────────────────────────────────────────────────────────────────────────────
const App = {
    state: {
        theme: localStorage.getItem('theme') || 'light',
        sidebarOpen: window.innerWidth > 1024,
        currentPage: 'dashboard',
        webhooks: [],
        stats: {
            totalWebhooks: 0,
            todayWebhooks: 0,
            successRate: 100,
            avgResponseTime: 0
        },
        health: null,
        config: null,
        refreshInterval: null,
        autoRefresh: true
    },

    // Initialize the application
    init() {
        this.applyTheme();
        this.bindEvents();
        this.loadInitialData();
        this.startAutoRefresh();
        this.initializeTooltips();
        console.log('VOLWebHook UI initialized');
    },

    // Apply theme
    applyTheme() {
        document.documentElement.setAttribute('data-theme', this.state.theme);
        this.updateThemeToggle();
    },

    // Toggle theme
    toggleTheme() {
        this.state.theme = this.state.theme === 'light' ? 'dark' : 'light';
        localStorage.setItem('theme', this.state.theme);
        this.applyTheme();
        Toast.show('Theme changed to ' + this.state.theme + ' mode', 'info');
    },

    updateThemeToggle() {
        const toggle = document.getElementById('theme-toggle');
        if (toggle) {
            const icon = toggle.querySelector('svg');
            if (icon) {
                icon.innerHTML = this.state.theme === 'dark'
                    ? '<path d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'
                    : '<path d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>';
            }
        }
    },

    // Bind global events
    bindEvents() {
        // Theme toggle
        document.getElementById('theme-toggle')?.addEventListener('click', () => this.toggleTheme());

        // Mobile menu toggle
        document.getElementById('mobile-menu-toggle')?.addEventListener('click', () => this.toggleSidebar());

        // Sidebar overlay click
        document.getElementById('sidebar-overlay')?.addEventListener('click', () => this.closeSidebar());

        // Navigation items
        document.querySelectorAll('.nav-item[data-page]').forEach(item => {
            item.addEventListener('click', (e) => {
                e.preventDefault();
                this.navigateTo(item.dataset.page);
            });
        });

        // Close sidebar on window resize if mobile
        window.addEventListener('resize', () => {
            if (window.innerWidth > 1024) {
                this.closeSidebar();
            }
        });

        // Search functionality
        document.getElementById('webhook-search')?.addEventListener('input', (e) => {
            this.filterWebhooks(e.target.value);
        });

        // Refresh button
        document.getElementById('refresh-btn')?.addEventListener('click', () => {
            this.refreshData();
        });

        // Auto-refresh toggle
        document.getElementById('auto-refresh-toggle')?.addEventListener('change', (e) => {
            this.state.autoRefresh = e.target.checked;
            if (this.state.autoRefresh) {
                this.startAutoRefresh();
            } else {
                this.stopAutoRefresh();
            }
        });

        // Modal close buttons
        document.querySelectorAll('.modal-close, [data-dismiss="modal"]').forEach(btn => {
            btn.addEventListener('click', () => Modal.closeAll());
        });

        // Keyboard shortcuts
        document.addEventListener('keydown', (e) => {
            // ESC to close modals
            if (e.key === 'Escape') {
                Modal.closeAll();
            }
            // Ctrl/Cmd + K for search
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                document.getElementById('webhook-search')?.focus();
            }
        });
    },

    // Toggle sidebar
    toggleSidebar() {
        this.state.sidebarOpen = !this.state.sidebarOpen;
        document.querySelector('.sidebar')?.classList.toggle('open', this.state.sidebarOpen);
        document.getElementById('sidebar-overlay')?.classList.toggle('active', this.state.sidebarOpen);
    },

    closeSidebar() {
        this.state.sidebarOpen = false;
        document.querySelector('.sidebar')?.classList.remove('open');
        document.getElementById('sidebar-overlay')?.classList.remove('active');
    },

    // Navigation
    navigateTo(page) {
        this.state.currentPage = page;

        // Update active nav item
        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.page === page);
        });

        // Show/hide pages
        document.querySelectorAll('.page').forEach(p => {
            p.classList.toggle('hidden', p.id !== `page-${page}`);
        });

        // Update page title
        const titles = {
            dashboard: 'Dashboard',
            webhooks: 'Webhooks',
            configuration: 'Configuration',
            logs: 'Logs',
            help: 'Help & Documentation'
        };
        document.getElementById('page-title').textContent = titles[page] || 'Dashboard';

        // Close sidebar on mobile
        if (window.innerWidth <= 1024) {
            this.closeSidebar();
        }

        // Load page-specific data
        this.loadPageData(page);
    },

    // Load initial data
    async loadInitialData() {
        await Promise.all([
            this.loadHealth(),
            this.loadStats(),
            this.loadWebhooks(),
            this.loadConfig()
        ]);
    },

    // Load page-specific data
    async loadPageData(page) {
        switch (page) {
            case 'dashboard':
                await Promise.all([this.loadHealth(), this.loadStats(), this.loadWebhooks(5)]);
                break;
            case 'webhooks':
                await this.loadWebhooks();
                break;
            case 'configuration':
                await this.loadConfig();
                break;
            case 'logs':
                await this.loadLogs();
                break;
        }
    },

    // Refresh data
    async refreshData() {
        const btn = document.getElementById('refresh-btn');
        const icon = btn?.querySelector('svg');
        if (icon) {
            icon.classList.add('animate-spin');
        }

        await this.loadPageData(this.state.currentPage);

        setTimeout(() => {
            icon?.classList.remove('animate-spin');
        }, 500);

        Toast.show('Data refreshed', 'success');
    },

    // Auto-refresh
    startAutoRefresh() {
        if (this.state.refreshInterval) {
            clearInterval(this.state.refreshInterval);
        }
        if (this.state.autoRefresh) {
            this.state.refreshInterval = setInterval(() => {
                this.loadHealth();
                if (this.state.currentPage === 'dashboard') {
                    this.loadStats();
                    this.loadWebhooks(5);
                }
            }, 30000); // 30 seconds
        }
    },

    stopAutoRefresh() {
        if (this.state.refreshInterval) {
            clearInterval(this.state.refreshInterval);
            this.state.refreshInterval = null;
        }
    },

    // Load health status
    async loadHealth() {
        try {
            const response = await fetch('/health');
            this.state.health = await response.json();
            this.renderHealth();
        } catch (error) {
            console.error('Failed to load health:', error);
            this.state.health = { status: 'unhealthy' };
            this.renderHealth();
        }
    },

    // Render health status
    renderHealth() {
        const badge = document.getElementById('health-badge');
        if (badge && this.state.health) {
            const isHealthy = this.state.health.status === 'healthy';
            badge.className = `status ${isHealthy ? 'status-online' : 'status-error'}`;
            badge.innerHTML = `
                <span class="status-dot"></span>
                <span>${isHealthy ? 'Healthy' : 'Unhealthy'}</span>
            `;
        }

        // Update security status cards
        if (this.state.health?.security) {
            const security = this.state.health.security;
            this.updateSecurityBadge('ip-allowlist-status', security.ipAllowlist);
            this.updateSecurityBadge('api-key-status', security.apiKey);
            this.updateSecurityBadge('hmac-status', security.hmac);
            this.updateSecurityBadge('rate-limit-status', security.rateLimit);
        }

        // Update uptime
        const uptimeEl = document.getElementById('uptime-value');
        if (uptimeEl && this.state.health?.uptime) {
            uptimeEl.textContent = this.formatUptime(this.state.health.uptime);
        }
    },

    updateSecurityBadge(id, enabled) {
        const el = document.getElementById(id);
        if (el) {
            el.className = `badge ${enabled ? 'badge-success badge-dot' : 'badge-neutral'}`;
            el.textContent = enabled ? 'Enabled' : 'Disabled';
        }
    },

    formatUptime(uptime) {
        // Parse TimeSpan format: "HH:MM:SS.ffffff" or "d.HH:MM:SS.ffffff"
        const parts = uptime.split(/[:.]/);
        if (parts.length >= 3) {
            const hours = parseInt(parts[0]);
            const minutes = parseInt(parts[1]);
            const seconds = parseInt(parts[2]);

            if (hours >= 24) {
                const days = Math.floor(hours / 24);
                const remainingHours = hours % 24;
                return `${days}d ${remainingHours}h ${minutes}m`;
            } else if (hours > 0) {
                return `${hours}h ${minutes}m ${seconds}s`;
            } else if (minutes > 0) {
                return `${minutes}m ${seconds}s`;
            } else {
                return `${seconds}s`;
            }
        }
        return uptime;
    },

    // Load stats
    async loadStats() {
        try {
            const response = await fetch('/api/ui/stats');
            if (response.ok) {
                this.state.stats = await response.json();
                this.renderStats();
            }
        } catch (error) {
            console.error('Failed to load stats:', error);
        }
    },

    // Render stats
    renderStats() {
        const stats = this.state.stats;

        document.getElementById('stat-total')?.textContent &&
            (document.getElementById('stat-total').textContent = this.formatNumber(stats.totalWebhooks));
        document.getElementById('stat-today')?.textContent &&
            (document.getElementById('stat-today').textContent = this.formatNumber(stats.todayWebhooks));
        document.getElementById('stat-success-rate')?.textContent &&
            (document.getElementById('stat-success-rate').textContent = `${stats.successRate}%`);
        document.getElementById('stat-avg-size')?.textContent &&
            (document.getElementById('stat-avg-size').textContent = this.formatBytes(stats.avgPayloadSize || 0));
    },

    formatNumber(num) {
        if (num >= 1000000) {
            return (num / 1000000).toFixed(1) + 'M';
        } else if (num >= 1000) {
            return (num / 1000).toFixed(1) + 'K';
        }
        return num.toString();
    },

    formatBytes(bytes) {
        if (bytes === 0) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
    },

    // Load webhooks
    async loadWebhooks(limit = 50) {
        try {
            const response = await fetch(`/api/ui/webhooks?limit=${limit}`);
            if (response.ok) {
                this.state.webhooks = await response.json();
                this.renderWebhooks();
            }
        } catch (error) {
            console.error('Failed to load webhooks:', error);
        }
    },

    // Render webhooks
    renderWebhooks() {
        const container = document.getElementById('webhooks-list');
        if (!container) return;

        if (this.state.webhooks.length === 0) {
            container.innerHTML = `
                <div class="empty-state">
                    <svg class="empty-state-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4"/>
                    </svg>
                    <h3 class="empty-state-title">No webhooks yet</h3>
                    <p class="empty-state-description">
                        Webhooks will appear here when external services send data to your endpoint.
                        Send a POST request to <code>/webhook</code> to get started.
                    </p>
                </div>
            `;
            return;
        }

        container.innerHTML = this.state.webhooks.map(webhook => `
            <div class="webhook-item" onclick="App.viewWebhook('${webhook.id}')">
                <div class="webhook-item-header">
                    <div class="webhook-item-id">
                        <span class="badge badge-primary">${webhook.httpMethod}</span>
                        <code>${webhook.id.substring(0, 8)}...</code>
                    </div>
                    <span class="webhook-item-time">${this.formatTime(webhook.receivedAtUtc)}</span>
                </div>
                <div class="webhook-item-meta">
                    <span class="webhook-item-ip">
                        <svg width="14" height="14" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9"/>
                        </svg>
                        ${webhook.sourceIpAddress}
                    </span>
                    <span class="webhook-item-size">
                        <svg width="14" height="14" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 7v10c0 2 1 3 3 3h10c2 0 3-1 3-3V7c0-2-1-3-3-3H7C5 4 4 5 4 7z"/>
                        </svg>
                        ${this.formatBytes(webhook.contentLength)}
                    </span>
                    <span class="badge ${webhook.isValidJson ? 'badge-success' : 'badge-warning'}">
                        ${webhook.isValidJson ? 'Valid JSON' : 'Invalid JSON'}
                    </span>
                </div>
            </div>
        `).join('');
    },

    formatTime(isoString) {
        const date = new Date(isoString);
        const now = new Date();
        const diff = now - date;

        if (diff < 60000) {
            return 'Just now';
        } else if (diff < 3600000) {
            return `${Math.floor(diff / 60000)}m ago`;
        } else if (diff < 86400000) {
            return `${Math.floor(diff / 3600000)}h ago`;
        } else {
            return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
        }
    },

    // Filter webhooks
    filterWebhooks(query) {
        const items = document.querySelectorAll('.webhook-item');
        const lowerQuery = query.toLowerCase();

        items.forEach(item => {
            const text = item.textContent.toLowerCase();
            item.style.display = text.includes(lowerQuery) ? '' : 'none';
        });
    },

    // View webhook detail
    async viewWebhook(id) {
        try {
            const response = await fetch(`/api/ui/webhooks/${id}`);
            if (response.ok) {
                const webhook = await response.json();
                this.showWebhookModal(webhook);
            }
        } catch (error) {
            console.error('Failed to load webhook:', error);
            Toast.show('Failed to load webhook details', 'error');
        }
    },

    showWebhookModal(webhook) {
        const modal = document.getElementById('webhook-modal');
        if (!modal) return;

        // Format headers
        const headersHtml = Object.entries(webhook.headers || {})
            .map(([key, values]) => `<div class="header-row"><span class="header-key">${key}:</span> <span class="header-value">${values.join(', ')}</span></div>`)
            .join('');

        // Format JSON body
        let bodyHtml = webhook.rawBody;
        if (webhook.isValidJson) {
            try {
                const parsed = JSON.parse(webhook.rawBody);
                bodyHtml = this.syntaxHighlight(JSON.stringify(parsed, null, 2));
            } catch (e) {
                bodyHtml = this.escapeHtml(webhook.rawBody);
            }
        } else {
            bodyHtml = this.escapeHtml(webhook.rawBody);
        }

        modal.querySelector('.modal-body').innerHTML = `
            <div class="webhook-detail">
                <div class="webhook-detail-section">
                    <h4>Request Information</h4>
                    <div class="detail-grid">
                        <div class="detail-item">
                            <span class="detail-label">Request ID</span>
                            <span class="detail-value font-mono">${webhook.id}</span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Received At</span>
                            <span class="detail-value">${new Date(webhook.receivedAtUtc).toLocaleString()}</span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Method</span>
                            <span class="detail-value"><span class="badge badge-primary">${webhook.httpMethod}</span></span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Path</span>
                            <span class="detail-value font-mono">${webhook.path}${webhook.queryString || ''}</span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Source IP</span>
                            <span class="detail-value font-mono">${webhook.sourceIpAddress}:${webhook.sourcePort}</span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Content Type</span>
                            <span class="detail-value font-mono">${webhook.contentType || 'N/A'}</span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Content Length</span>
                            <span class="detail-value">${this.formatBytes(webhook.contentLength)}</span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Valid JSON</span>
                            <span class="detail-value">
                                <span class="badge ${webhook.isValidJson ? 'badge-success' : 'badge-warning'}">
                                    ${webhook.isValidJson ? 'Yes' : 'No'}
                                </span>
                            </span>
                        </div>
                    </div>
                </div>

                <div class="webhook-detail-section">
                    <h4>Headers</h4>
                    <div class="headers-list">${headersHtml}</div>
                </div>

                <div class="webhook-detail-section">
                    <h4>Body</h4>
                    <div class="code-block">
                        <div class="code-header">
                            <span class="code-language">${webhook.isValidJson ? 'JSON' : 'Text'}</span>
                            <button class="code-copy" onclick="App.copyToClipboard('${this.escapeHtml(webhook.rawBody).replace(/'/g, "\\'")}')">
                                Copy
                            </button>
                        </div>
                        <div class="code-content">
                            <pre><code>${bodyHtml}</code></pre>
                        </div>
                    </div>
                </div>
            </div>
        `;

        Modal.open('webhook-modal');
    },

    syntaxHighlight(json) {
        json = this.escapeHtml(json);
        return json.replace(/("(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g, (match) => {
            let cls = 'json-number';
            if (/^"/.test(match)) {
                if (/:$/.test(match)) {
                    cls = 'json-key';
                    match = match.slice(0, -1) + '</span>:';
                    return `<span class="${cls}">${match}`;
                } else {
                    cls = 'json-string';
                }
            } else if (/true|false/.test(match)) {
                cls = 'json-boolean';
            } else if (/null/.test(match)) {
                cls = 'json-null';
            }
            return `<span class="${cls}">${match}</span>`;
        });
    },

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    },

    copyToClipboard(text) {
        navigator.clipboard.writeText(text).then(() => {
            Toast.show('Copied to clipboard', 'success');
        }).catch(() => {
            Toast.show('Failed to copy', 'error');
        });
    },

    // Load configuration
    async loadConfig() {
        try {
            const response = await fetch('/api/ui/config');
            if (response.ok) {
                this.state.config = await response.json();
                this.renderConfig();
            }
        } catch (error) {
            console.error('Failed to load config:', error);
        }
    },

    // Render configuration
    renderConfig() {
        const config = this.state.config;
        if (!config) return;

        // Webhook settings
        this.setInputValue('config-max-payload', config.webhook?.maxPayloadSizeMB || 10);
        this.setInputValue('config-storage-path', config.webhook?.payloadStoragePath || './data/webhooks');
        this.setInputValue('config-retention-days', config.webhook?.payloadRetentionDays || 30);
        this.setCheckboxValue('config-persistence', config.webhook?.enablePayloadPersistence ?? true);
        this.setCheckboxValue('config-always-200', config.webhook?.alwaysReturn200 ?? true);

        // Security settings
        this.setCheckboxValue('config-ip-enabled', config.security?.ipAllowlist?.enabled ?? false);
        this.setCheckboxValue('config-apikey-enabled', config.security?.apiKey?.enabled ?? false);
        this.setCheckboxValue('config-hmac-enabled', config.security?.hmac?.enabled ?? false);
        this.setCheckboxValue('config-ratelimit-enabled', config.security?.rateLimit?.enabled ?? false);

        if (config.security?.rateLimit) {
            this.setInputValue('config-rate-per-minute', config.security.rateLimit.requestsPerMinute || 100);
            this.setInputValue('config-rate-per-hour', config.security.rateLimit.requestsPerHour || 1000);
        }

        // Toggle visibility of dependent fields
        this.toggleConfigSection('ip-settings', config.security?.ipAllowlist?.enabled);
        this.toggleConfigSection('apikey-settings', config.security?.apiKey?.enabled);
        this.toggleConfigSection('hmac-settings', config.security?.hmac?.enabled);
        this.toggleConfigSection('ratelimit-settings', config.security?.rateLimit?.enabled);
    },

    setInputValue(id, value) {
        const el = document.getElementById(id);
        if (el) el.value = value;
    },

    setCheckboxValue(id, checked) {
        const el = document.getElementById(id);
        if (el) el.checked = checked;
    },

    toggleConfigSection(id, show) {
        const el = document.getElementById(id);
        if (el) {
            el.style.display = show ? '' : 'none';
        }
    },

    // Save configuration
    async saveConfig() {
        const config = {
            webhook: {
                maxPayloadSizeMB: parseInt(document.getElementById('config-max-payload')?.value) || 10,
                payloadStoragePath: document.getElementById('config-storage-path')?.value || './data/webhooks',
                payloadRetentionDays: parseInt(document.getElementById('config-retention-days')?.value) || 30,
                enablePayloadPersistence: document.getElementById('config-persistence')?.checked ?? true,
                alwaysReturn200: document.getElementById('config-always-200')?.checked ?? true
            },
            security: {
                ipAllowlist: {
                    enabled: document.getElementById('config-ip-enabled')?.checked ?? false
                },
                apiKey: {
                    enabled: document.getElementById('config-apikey-enabled')?.checked ?? false
                },
                hmac: {
                    enabled: document.getElementById('config-hmac-enabled')?.checked ?? false
                },
                rateLimit: {
                    enabled: document.getElementById('config-ratelimit-enabled')?.checked ?? false,
                    requestsPerMinute: parseInt(document.getElementById('config-rate-per-minute')?.value) || 100,
                    requestsPerHour: parseInt(document.getElementById('config-rate-per-hour')?.value) || 1000
                }
            }
        };

        try {
            const response = await fetch('/api/ui/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(config)
            });

            if (response.ok) {
                Toast.show('Configuration saved successfully', 'success');
                await this.loadHealth();
            } else {
                const error = await response.text();
                Toast.show(`Failed to save: ${error}`, 'error');
            }
        } catch (error) {
            Toast.show('Failed to save configuration', 'error');
        }
    },

    // Load logs
    async loadLogs() {
        try {
            const response = await fetch('/api/ui/logs?limit=100');
            if (response.ok) {
                const logs = await response.json();
                this.renderLogs(logs);
            }
        } catch (error) {
            console.error('Failed to load logs:', error);
        }
    },

    renderLogs(logs) {
        const container = document.getElementById('logs-list');
        if (!container) return;

        if (!logs || logs.length === 0) {
            container.innerHTML = `
                <div class="empty-state">
                    <svg class="empty-state-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
                    </svg>
                    <h3 class="empty-state-title">No logs available</h3>
                    <p class="empty-state-description">Log entries will appear here as the service processes requests.</p>
                </div>
            `;
            return;
        }

        container.innerHTML = logs.map(log => `
            <div class="log-entry log-${log.level?.toLowerCase() || 'info'}">
                <span class="log-time">${this.formatTime(log.timestamp)}</span>
                <span class="log-level badge badge-${this.getLogLevelBadge(log.level)}">${log.level}</span>
                <span class="log-message">${this.escapeHtml(log.message)}</span>
            </div>
        `).join('');
    },

    getLogLevelBadge(level) {
        const badges = {
            'error': 'danger',
            'warning': 'warning',
            'information': 'info',
            'debug': 'neutral'
        };
        return badges[level?.toLowerCase()] || 'neutral';
    },

    // Initialize tooltips
    initializeTooltips() {
        document.querySelectorAll('[data-tooltip]').forEach(el => {
            el.addEventListener('mouseenter', (e) => {
                const tooltip = document.createElement('div');
                tooltip.className = 'tooltip';
                tooltip.textContent = e.target.dataset.tooltip;
                document.body.appendChild(tooltip);

                const rect = e.target.getBoundingClientRect();
                tooltip.style.top = `${rect.top - tooltip.offsetHeight - 8}px`;
                tooltip.style.left = `${rect.left + (rect.width - tooltip.offsetWidth) / 2}px`;
            });

            el.addEventListener('mouseleave', () => {
                document.querySelectorAll('.tooltip').forEach(t => t.remove());
            });
        });
    }
};

// ─────────────────────────────────────────────────────────────────────────────
// Modal Helper
// ─────────────────────────────────────────────────────────────────────────────
const Modal = {
    open(id) {
        const modal = document.getElementById(id);
        if (modal) {
            modal.classList.add('active');
            document.body.style.overflow = 'hidden';
        }
    },

    close(id) {
        const modal = document.getElementById(id);
        if (modal) {
            modal.classList.remove('active');
            document.body.style.overflow = '';
        }
    },

    closeAll() {
        document.querySelectorAll('.modal-backdrop.active').forEach(modal => {
            modal.classList.remove('active');
        });
        document.body.style.overflow = '';
    }
};

// ─────────────────────────────────────────────────────────────────────────────
// Toast Notifications
// ─────────────────────────────────────────────────────────────────────────────
const Toast = {
    container: null,

    init() {
        if (!this.container) {
            this.container = document.createElement('div');
            this.container.className = 'toast-container';
            document.body.appendChild(this.container);
        }
    },

    show(message, type = 'info', duration = 4000) {
        this.init();

        const icons = {
            success: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/>',
            error: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>',
            warning: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"/>',
            info: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/>'
        };

        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;
        toast.innerHTML = `
            <svg class="toast-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                ${icons[type] || icons.info}
            </svg>
            <div class="toast-content">
                <span class="toast-message">${message}</span>
            </div>
            <button class="toast-close" onclick="this.parentElement.remove()">
                <svg width="16" height="16" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>
                </svg>
            </button>
        `;

        this.container.appendChild(toast);

        setTimeout(() => {
            toast.style.animation = 'slideInRight 0.3s ease reverse';
            setTimeout(() => toast.remove(), 300);
        }, duration);
    }
};

// ─────────────────────────────────────────────────────────────────────────────
// Initialize on DOM Ready
// ─────────────────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    App.init();
});

// Export for global access
window.App = App;
window.Modal = Modal;
window.Toast = Toast;
