// Jellyfin Migrator Plugin Configuration Page JavaScript
// Minimal zip builder using fflate (embedded)
// fflate v0.8.2 (subset): unzip not included, only zip
/* eslint-disable */
const fflate=(()=>{function h(e,t){return Object.setPrototypeOf(e,t),e}function p(e,t){e.push(t>>24&255,t>>16&255,t>>8&255,255&t)}function y(e,t){for(let r=0;r<4;r++)e.push(255&t),t>>=8}function w(e){let t=0;for(let r=0;r<e.length;r++)t=t>>>0+e[r]>>>0,t>>>0>=4294967296&&(t-=4294967296);return t>>>0}function C(e){let t=~e>>>0;return y([],t)}function S(e,t,r){let n=[];y(n,67324752),y(n,t),y(n,20),y(n,0),y(n,0),y(n,0),y(n,0),y(n,0),y(n,0),y(n,r.length),y(n,0),n.push(...e),n.push(...r);return n}function P(e,t,r,n,i){let a=[];y(a,33639248),y(a,t),y(a,20),y(a,0),y(a,0),y(a,0),y(a,0),y(a,0),y(a,0),y(a,r.length),y(a,0),y(a,0),y(a,0),y(a,0),y(a,n),y(a,0),a.push(...e);let s=i;return a.push(...s),a}function D(e){let t=0;for(let r=0;r<e.length;r++)t+=e[r].length;return t}function T(e){return new Blob([new Uint8Array(e)],{type:'application/zip'})}function B(e){let t=0;for(let r of e)t+=r.length;return t}return{zipSync:function(e){let t=[],r=[],n=[],i=0;for(let a of e){let s=new TextEncoder().encode(a.name),o=typeof a.data=='string'?new TextEncoder().encode(a.data):new Uint8Array(a.data),l=o.length,c=w(o),u=S(s,0,[],o);r.push(u);let f=P(s,0,[],l,[]);n.push(f),i+=u.length}let a=[];for(let s of r)a.push(...s);let o=D(n),l=a.length;for(let s of n)a.push(...s);y(a,101010256),y(a,0),y(a,0),y(a,r.length),y(a,r.length),y(a,o),y(a,l),y(a,0);return T(a)}}})();
/* eslint-enable */

const TemplateConfig = {
    pluginUniqueId: 'eb5d7894-8eef-4b36-aa6f-5d124e828ce1'
};

// Download ZIP from configuration base64
function downloadServerZipFromConfig(cfg) {
    var b64 = (cfg && cfg.LastExportZipBase64) || '';
    if (!b64) { Dashboard.alert({ title: 'Export', message: 'ZIP not ready yet. Please wait a moment.' }); return; }
    try {
        var byteChars = atob(b64);
        var byteNumbers = new Array(byteChars.length);
        for (var i = 0; i < byteChars.length; i++) { byteNumbers[i] = byteChars.charCodeAt(i); }
        var byteArray = new Uint8Array(byteNumbers);
        var blob = new Blob([byteArray], { type: 'application/zip' });
        var a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        var ts = cfg.LastExportUtc ? new Date(cfg.LastExportUtc).toISOString().replace(/[:T]/g,'-').split('.')[0] : '';
        a.download = ts ? ('jellyfin-export-' + ts + '.zip') : 'jellyfin-export.zip';
        document.body.appendChild(a); a.click(); a.remove();
    } catch (e) { console.error('Failed to decode ZIP', e); Dashboard.alert({ title: 'Export', message: 'Failed to decode ZIP. See console.'}); }
}

// Page load handler
document.querySelector('#TemplateConfigPage')
    .addEventListener('pageshow', function() {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId).then(function (config) {
            document.querySelector('#IncludeUsers').checked = config.IncludeUsers === true;
            document.querySelector('#IncludeUserPasswordHashes').checked = config.IncludeUserPasswordHashes === true;
            document.querySelector('#IncludeLibraries').checked = config.IncludeLibraries === true;
            document.querySelector('#IncludePermissions').checked = config.IncludePermissions === true;
            document.querySelector('#IncludeWatchHistory').checked = config.IncludeWatchHistory === true;
            document.querySelector('#IncludeDevices').checked = config.IncludeDevices === true;

            // Render dynamic lists
            loadUsersAndLibraries(config);
            try { renderExportLog(config); } catch (e) {}
        });
    });

// Form submission handler
document.querySelector('#TemplateConfigForm')
    .addEventListener('submit', function(e) {
    Dashboard.showLoadingMsg();
    ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId).then(function (config) {
        config.IncludeUsers = document.querySelector('#IncludeUsers').checked;
        config.IncludeUserPasswordHashes = document.querySelector('#IncludeUserPasswordHashes').checked;
        config.IncludeLibraries = document.querySelector('#IncludeLibraries').checked;
        config.IncludePermissions = document.querySelector('#IncludePermissions').checked;
        config.IncludeWatchHistory = document.querySelector('#IncludeWatchHistory').checked;
        config.IncludeDevices = document.querySelector('#IncludeDevices').checked;

        var selectedUserIds = [];
        var selectedUsernames = [];
        document.querySelectorAll('#UsersList input[type="checkbox"][data-kind="user"]:checked').forEach(function (el) {
            selectedUserIds.push(el.value);
            selectedUsernames.push(el.getAttribute('data-username'));
        });
        config.SelectedUserIds = selectedUserIds;
        config.SelectedUsernames = selectedUsernames;

        var selectedLibIds = [];
        var selectedLibPaths = [];
        document.querySelectorAll('#LibrariesList input[type="checkbox"][data-kind="library"]:checked').forEach(function (el) {
            selectedLibIds.push(el.value);
            try {
                var paths = JSON.parse(decodeURIComponent(el.getAttribute('data-paths') || '[]'));
                (paths || []).forEach(function (p) { selectedLibPaths.push(p); });
            } catch {}
        });
        config.SelectedLibraryIds = selectedLibIds;
        config.SelectedLibraryPaths = selectedLibPaths;

        // Determine mode from active tab
        var modeSubmit = (function(){
            var vt = document.getElementById('VerifyTab');
            var it = document.getElementById('ImportTab');
            if (vt && vt.style && vt.style.display !== 'none') return 'Verify';
            if (it && it.style && it.style.display !== 'none') return 'Import';
            return 'Export';
        })();
        config.Mode = modeSubmit;

        ApiClient.updatePluginConfiguration(TemplateConfig.pluginUniqueId, config).then(function (result) {
            Dashboard.processPluginConfigurationUpdateResult(result);
        });
    });

    e.preventDefault();
    return false;
});

// Run export/import/verify button handler
document.querySelector('#RunExportNow')
    .addEventListener('click', function () {
        Dashboard.showLoadingMsg();
        var $status = document.querySelector('#ExportRunStatus');

        // First save the configuration
        ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId).then(function (config) {
            // Determine mode from active tab
            var mode = (function(){
                var vt = document.getElementById('VerifyTab');
                var it = document.getElementById('ImportTab');
                if (vt && vt.style && vt.style.display !== 'none') return 'Verify';
                if (it && it.style && it.style.display !== 'none') return 'Import';
                return 'Export';
            })();
            config.Mode = mode;

            config.IncludeUsers = document.querySelector('#IncludeUsers').checked;
            config.IncludeUserPasswordHashes = document.querySelector('#IncludeUserPasswordHashes').checked;
            config.IncludeLibraries = document.querySelector('#IncludeLibraries').checked;
            config.IncludePermissions = document.querySelector('#IncludePermissions').checked;
            config.IncludeWatchHistory = document.querySelector('#IncludeWatchHistory').checked;
            config.IncludeDevices = document.querySelector('#IncludeDevices').checked;

            var selectedUserIds = [];
            var selectedUsernames = [];
            document.querySelectorAll('#UsersList input[type="checkbox"][data-kind="user"]:checked').forEach(function (el) {
                selectedUserIds.push(el.value);
                selectedUsernames.push(el.getAttribute('data-username'));
            });
            config.SelectedUserIds = selectedUserIds;
            config.SelectedUsernames = selectedUsernames;

            var selectedLibIds = [];
            var selectedLibPaths = [];
            document.querySelectorAll('#LibrariesList input[type="checkbox"][data-kind="library"]:checked').forEach(function (el) {
                selectedLibIds.push(el.value);
                try {
                    var paths = JSON.parse(decodeURIComponent(el.getAttribute('data-paths') || '[]'));
                    (paths || []).forEach(function (p) { selectedLibPaths.push(p); });
                } catch {}
            });
            config.SelectedLibraryIds = selectedLibIds;
            config.SelectedLibraryPaths = selectedLibPaths;

            // Export directory is server-side default now
            config.VerifyDirectory = document.querySelector('#VerifyDirectory') ? document.querySelector('#VerifyDirectory').value : '';

            return ApiClient.updatePluginConfiguration(TemplateConfig.pluginUniqueId, config);
        }).then(function (result) {
            // Configuration saved successfully
            console.log('Configuration saved:', result);

            // Wait a moment for the save to propagate, then run the export
            return new Promise(function(resolve) {
                setTimeout(function() {
                    resolve();
                }, 1000); // 1 second delay
            });
        }).then(function () {
            // Now run the task (Export/Verify)
            return ApiClient.getScheduledTasks();
        }).then(function (tasks) {
            var task = (tasks || []).find(function (t) { return t.Key === 'JellyfinMigratorExport'; });
            if (!task) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert({ title: 'Migrator', message: 'Migration task not found.' });
                return Promise.reject('Task not found');
            }
            return ApiClient.startScheduledTask(task.Id).then(function () { return task; });
        }).then(function () {
            Dashboard.hideLoadingMsg();
             $status.textContent =  'RUNNING.';
            // Light polling to update status until completion (max 5 minutes)
            var started = Date.now();
            var poll = function () {
                ApiClient.getScheduledTasks().then(function (tasks) {
                    var t = (tasks || []).find(function (x) { return x.Key === 'JellyfinMigratorExport'; });
                    if (!t) { return; }
                    var state = (t.State || t.Status || '').toString().toLowerCase();
                    if (state.indexOf('running') !== -1) {
                         $status.textContent =  'RUNNING.';
                    } else if (state.indexOf('idle') !== -1 || state.indexOf('completed') !== -1 || state === '') {
                        $status.textContent = 'COMPLETED';
                        var $dl = document.getElementById('DownloadExportContainer');
                        if ($dl) { $dl.style.display = 'block'; }
                        ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId).then(function(cfg){ downloadServerZipFromConfig(cfg); });
                        return; // stop polling
                    }
                    if (Date.now() - started < 5 * 60 * 1000) {
                        setTimeout(poll, 2000);
                    }
                }).catch(function () {
                    if (Date.now() - started < 5 * 60 * 1000) {
                        setTimeout(poll, 4000);
                    }
                });
            };
            setTimeout(poll, 1500);
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            console.error('Failed to save settings or start export', err);
            Dashboard.alert({ title: 'Migrator', message: 'Failed to save settings or start export.' });
        });
    });

// Live log updater: polls plugin configuration while status shows RUNNING
(function setupLiveLogUpdater() {
    var timerId = null;
    function updateLog() {
        ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId)
            .then(function (cfg) { try { renderExportLog(cfg); } catch (e) {} });
    }
    function checkStatus() {
        var $status = document.querySelector('#ExportRunStatus');
        var s = ($status && $status.textContent || '').toLowerCase();
        if (s.indexOf('running') !== -1) {
            if (!timerId) {
                timerId = setInterval(updateLog, 2000);
            }
        } else {
            if (timerId) {
                clearInterval(timerId);
                timerId = null;
                updateLog();
            }
        }
    }
    setInterval(checkStatus, 1000);
})();

// Load users and libraries
function loadUsersAndLibraries(config) {
    var $users = document.querySelector('#UsersList');
    var $libs = document.querySelector('#LibrariesList');
    $users.innerHTML = '<div>Loading users…</div>';
    $libs.innerHTML = '<div>Loading libraries…</div>';

    var usersUrl = ApiClient.getUrl('Users');
    var libsUrl = ApiClient.getUrl('Library/VirtualFolders');

    Promise.all([
        ApiClient.fetch({ url: usersUrl, method: 'GET' }).then(function (r) { return r.json(); }),
        ApiClient.fetch({ url: libsUrl, method: 'GET' }).then(function (r) { return r.json(); })
    ]).then(function (results) {
        var users = results[0] || [];
        var libraries = results[1] || [];

        // Users
        var uHtml = users.map(function (u) {
            var checked = (config.SelectedUserIds || []).indexOf(u.Id) !== -1 ? 'checked' : '';
            return '<label class="emby-checkbox-label" style="display:block;">' +
                '<input data-kind="user" type="checkbox" is="emby-checkbox" value="' + (u.Id || '') + '" data-username="' + (u.Name || '') + '" ' + checked + ' />' +
                '<span>' + (u.Name || '') + '</span>' +
            '</label>';
        }).join('');
        $users.innerHTML = '<div class="fieldDescription">Select users to include</div>' + uHtml;

        // Libraries
        var lHtml = libraries.map(function (l) {
            var checked = (config.SelectedLibraryIds || []).indexOf(l.ItemId) !== -1 ? 'checked' : '';
            var dataPaths = encodeURIComponent(JSON.stringify(l.Locations || []));
            return '<label class="emby-checkbox-label" style="display:block;">' +
                '<input data-kind="library" type="checkbox" is="emby-checkbox" value="' + (l.ItemId || '') + '" data-paths="' + dataPaths + '" ' + checked + ' />' +
                '<span>' + (l.Name || '') + ' (' + (l.CollectionType || 'Mixed') + ')</span>' +
            '</label>';
        }).join('');
        $libs.innerHTML = '<div class="fieldDescription">Select libraries to include</div>' + lHtml;
    }).catch(function (err) {
        console.error('Failed to load users/libraries', err);
        $users.innerHTML = '<div class="fieldDescription">Failed to load users</div>';
        $libs.innerHTML = '<div class="fieldDescription">Failed to load libraries</div>';
    }).finally(function () {
        Dashboard.hideLoadingMsg();
    });
}

// Render export log
function renderExportLog(config) {
    var $log = document.querySelector('#ExportLog');
    var $meta = document.querySelector('#ExportMeta');
    if (!$log || !$meta) { return; }
    var log = (config.LastExportLog || '').toString();
    $log.textContent = log.trim() ? log : '(no recent log)';
    var parts = [];
    if (config.LastExportUtc) { try { parts.push('Completed: ' + new Date(config.LastExportUtc).toLocaleString()); } catch(e) {} }
    if (config.LastExportPath) { parts.push('Path: ' + config.LastExportPath); }
    $meta.textContent = parts.join(' | ');
}

// Tabs and browse dialogs setup
(function setupTabsAndBrowse() {
    var $tabExport = document.getElementById('TabExport');
    var $tabVerify = document.getElementById('TabVerify');
    var $tabImport = document.getElementById('TabImport');
    var $export = document.getElementById('ExportTab');
    var $verify = document.getElementById('VerifyTab');
    var $import = document.getElementById('ImportTab');
    var $runText = document.getElementById('RunButtonText');

    function setActive(tab) {
        if ($export) { $export.style.display = tab === 'Export' ? 'block' : 'none'; }
        if ($verify) { $verify.style.display = tab === 'Verify' ? 'block' : 'none'; }
        if ($import) { $import.style.display = tab === 'Import' ? 'block' : 'none'; }
        [$tabExport,$tabVerify,$tabImport].filter(Boolean).forEach(function(btn){
            btn.classList.remove('raised');
            if (!btn.classList.contains('button-flat')) btn.classList.add('button-flat');
        });
        if (tab === 'Export' && $tabExport) { $tabExport.classList.add('raised'); }
        if (tab === 'Verify' && $tabVerify) { $tabVerify.classList.add('raised'); }
        if (tab === 'Import' && $tabImport) { $tabImport.classList.add('raised'); }
        if ($runText) { $runText.textContent = tab === 'Verify' ? 'Save & Run Verify' : (tab === 'Import' ? 'Save Config' : 'Save & Run Export'); }
        if (tab === 'Import') {
            var hold = document.getElementById('ImportHolding'); if (hold) hold.style.display = 'none';
            var dirEl = document.getElementById('ImportDirectory');
            if (dirEl && dirEl.closest) {
                var ctn = dirEl.closest('.inputContainer'); if (ctn) { ctn.style.display = 'none'; }
            }
            var bid = document.getElementById('BrowseImportDir'); if (bid) { bid.style.display = 'none'; }
        }
    }

    ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId).then(function(cfg){
        var desired = (cfg.Mode || 'Export');
        if (desired === 'Verify' && !$tabVerify) { desired = 'Export'; }
        setActive(desired);
        var vd = document.getElementById('VerifyDirectory'); if (vd) vd.value = cfg.VerifyDirectory || '';
    });

    if ($tabExport) $tabExport.addEventListener('click', function(){ setActive('Export'); });
    if ($tabVerify) $tabVerify.addEventListener('click', function(){ setActive('Verify'); });
    if ($tabImport) $tabImport.addEventListener('click', function(){ setActive('Import'); });

    // Normalize browse buttons
    ['BrowseVerifyDir'].forEach(function(id){
        var b = document.getElementById(id);
        if (b) {
            b.classList.remove('raised');
            if (!b.classList.contains('emby-button')) b.classList.add('emby-button');
            if (!b.classList.contains('button-flat')) b.classList.add('button-flat');
            b.textContent = 'Browse…';
        }
    });

    // Clean up action button classes
    var runBtn = document.getElementById('RunExportNow');
    if (runBtn) { runBtn.classList.remove('btnRefresh'); }

    // Jellyfin directory browser
    function openJfDirectoryPicker(targetInputId) {
        var input = document.getElementById(targetInputId);
        var current = (input && input.value) || '';

        function applyPath(path) {
            if (input) { input.value = path || ''; }
        }

        function openCustomDirectoryModal() {
            var $modal = document.getElementById('DirBrowser');
            if ($modal) { $modal.remove(); }
            $modal = document.createElement('div');
            $modal.id = 'DirBrowser';
            $modal.style.position = 'fixed';
            $modal.style.left = '0';
            $modal.style.top = '0';
            $modal.style.right = '0';
            $modal.style.bottom = '0';
            $modal.style.background = 'rgba(0,0,0,0.6)';
            $modal.style.zIndex = '9999';
            $modal.innerHTML = '<div style="position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:560px;max-height:72vh;overflow:auto;background:#1b1b1b;padding:12px;border-radius:6px;">\
                <div style="display:flex;justify-content:space-between;align-items:center;gap:8px;">\
                    <div style="font-weight:600;">Select Folder</div>\
                    <button is="emby-button" type="button" class="emby-button button-flat" id="DirClose">Close</button>\
                </div>\
                <div style="margin-top:8px;display:flex;gap:6px;align-items:center;">\
                    <button is="emby-button" type="button" class="emby-button button-flat" id="DirUp">Up</button>\
                    <input id="DirPath" is="emby-input" type="text" style="flex:1;" placeholder="Enter path"/>\
                    <button is="emby-button" type="button" class="emby-button button-flat" id="DirGo">Go</button>\
                </div>\
                <div id="DirList" style="margin-top:8px;"></div>\
            </div>';
            document.body.appendChild($modal);
            $modal.addEventListener('click', function(e){ if (e.target.id === 'DirBrowser' || e.target.id === 'DirClose') $modal.remove(); });

            var $list = $modal.querySelector('#DirList');
            var $path = $modal.querySelector('#DirPath');
            var currentPath = '';

            function renderEntries(entries) {
                $list.innerHTML = '<div class="paperList" role="list">' + (entries || []).map(function(e){
                    var name = e.Name || e.name || '';
                    var path = e.Path || e.path || e.FullName || e.fullName || e.FullPath || e.fullPath || name;
                    var openBtn = '<button type="button" is="emby-button" class="listItem listItemButton paperListItem" data-path="' + encodeURIComponent(path) + '"><div class="listItemBody"><div class="listItemBodyText">' + name + '</div></div><span class="material-icons" aria-hidden="true">chevron_right</span></button>';
                    var selectBtn = '<button type="button" is="emby-button" class="emby-button button-flat" data-select="' + encodeURIComponent(path) + '">Select</button>';
                    return '<div style="display:flex;justify-content:space-between;align-items:center;padding:6px 8px;">' + openBtn + selectBtn + '</div>';
                }).join('') + '</div>';

                Array.from($list.querySelectorAll('button[data-path]')).forEach(function(btn){
                    btn.addEventListener('click', function(){ loadDir(decodeURIComponent(this.getAttribute('data-path'))); });
                });
                Array.from($list.querySelectorAll('button[data-select]')).forEach(function(btn){
                    btn.addEventListener('click', function(){ var p = decodeURIComponent(this.getAttribute('data-select')); applyPath(p); $modal.remove(); });
                });
            }

            function loadDrives() {
                var url = ApiClient.getUrl('Environment/Drives');
                $list.innerHTML = 'Loading drives…';
                ApiClient.fetch({ url: url, method: 'GET' }).then(function(r){ return r.json(); }).then(function(data){
                    currentPath = '';
                    $path.value = '';
                    renderEntries((data || []).map(function(d){ return { Name: (d.Name || d.Path || d.name || d.path), Path: (d.Path || d.Name || d.FullName || d.fullName || d.FullPath || d.fullPath) }; }));
                }).catch(function(){ $list.innerHTML = 'Failed to load drives'; });
            }

            function loadDir(path) {
                currentPath = path || '';
                $path.value = currentPath;
                if (!currentPath) { loadDrives(); return; }
                var url = ApiClient.getUrl('Environment/DirectoryContents', { path: currentPath, includeFiles: false });
                $list.innerHTML = 'Loading…';
                ApiClient.fetch({ url: url, method: 'GET' }).then(function(r){ return r.json(); }).then(function(items){
                    renderEntries(items || []);
                }).catch(function(){ $list.innerHTML = 'Failed to load directory'; });
            }

            $modal.querySelector('#DirGo').addEventListener('click', function(){ loadDir($path.value); });
            $modal.querySelector('#DirUp').addEventListener('click', function(){
                if (!currentPath) return; var p=currentPath.replace(/\\+$/, ''); var i=p.lastIndexOf('\\'); if (i<=0){ loadDrives(); } else { loadDir(p.substring(0,i+1)); }
            });

            loadDir(current);
        }

        function fallbackPrompt() {
            var entered = window.prompt('Enter directory path', current);
            if (entered != null) { applyPath(entered); }
        }

        function tryShow(ctor) {
            try {
                if (!ctor) return false;
                var picker = new ctor();
                picker.show({
                    callback: applyPath,
                    serverId: (typeof ApiClient.serverId === 'function') ? ApiClient.serverId() : null,
                    enableFiles: false,
                    path: current
                });
                return true;
            } catch (e) {
                console.warn('DirectoryBrowser show failed', e);
                return false;
            }
        }

        if (window.DirectoryBrowser && tryShow(window.DirectoryBrowser)) {
            return;
        }

        var moduleIds = [
            'directorybrowser',
            'components/directorybrowser/directorybrowser',
            'emby/directorybrowser/directorybrowser',
            'scripts/directorybrowser',
            'admin/directorybrowser/directorybrowser'
        ];

        var triedAll = false;
        var idx = 0;
        function tryNext() {
            if (idx >= moduleIds.length) {
                triedAll = true;
                try {
                    require(['dialogHelper'], function (dh) {
                        if (dh && (dh.showDirectoryPicker || dh.showDirectoryChooser)) {
                            var fn = dh.showDirectoryPicker || dh.showDirectoryChooser;
                            try {
                                fn({ callback: applyPath, path: current, enableFiles: false, serverId: (typeof ApiClient.serverId === 'function') ? ApiClient.serverId() : null });
                            } catch (e) {
                                console.warn('dialogHelper directory picker failed', e);
                                openCustomDirectoryModal();
                            }
                        } else {
                            openCustomDirectoryModal();
                        }
                    });
                } catch (e) {
                    openCustomDirectoryModal();
                }
                return;
            }
            var id = moduleIds[idx++];
            try {
                require([id], function (mod) {
                    var ctor = (mod && (mod.DirectoryBrowser || mod.default || mod));
                    if (!tryShow(ctor)) {
                        tryNext();
                    }
                }, function () { tryNext(); });
            } catch (e) {
                tryNext();
            }
        }

        tryNext();
    }

    var bv = document.getElementById('BrowseVerifyDir'); if (bv) { bv.addEventListener('click', function(){ openJfDirectoryPicker('VerifyDirectory'); }); }

    // Upload ZIP & analyze
    var uploadBtn = document.getElementById('UploadImportZip');
    if (uploadBtn) {
        uploadBtn.addEventListener('click', function(){
            var fileInput = document.getElementById('ImportZipFile');
            var statusEl = document.getElementById('ImportAnalyzeStatus');
            var breakdownEl = document.getElementById('ImportBreakdown');
            if (!fileInput || !fileInput.files || fileInput.files.length === 0) {
                Dashboard.alert({ title: 'Import', message: 'Please choose a ZIP file first.' });
                return;
            }
            var file = fileInput.files[0];
            statusEl.style.display = 'block';
            statusEl.textContent = 'Uploading and analyzing...';
            breakdownEl.style.display = 'none';
            breakdownEl.innerHTML = '';

            var reader = new FileReader();
            reader.onload = function(){
                try {
                    var arr = new Uint8Array(reader.result);
                    var b64 = btoa(Array.prototype.map.call(arr, function(ch){ return String.fromCharCode(ch); }).join(''));
                    ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId).then(function(cfg){
                        cfg.LastImportZipBase64 = b64;
                        cfg.Mode = 'Import';
                        return ApiClient.updatePluginConfiguration(TemplateConfig.pluginUniqueId, cfg);
                    }).then(function(){
                        return ApiClient.getScheduledTasks();
                    }).then(function(tasks){
                        var t = tasks.find(function(x){ return x && (x.Key === 'JellyfinMigratorExport' || x.Name === 'Migration: Export Data'); });
                        if (!t) throw new Error('Import task not found');
                        return ApiClient.startScheduledTask(t.Id).then(function(){ return t; });
                    }).then(function(){
                        // Poll configuration for analysis
                        var attempts = 0; var maxAttempts = 40;
                        function poll(){
                            attempts++;
                            ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId).then(function(cfg){
                                if (cfg && cfg.LastImportAnalysisJson){
                                    var data = {};
                                    try { data = JSON.parse(cfg.LastImportAnalysisJson); } catch(e) { console.warn('Bad analysis JSON', e); }
                                    statusEl.textContent = 'Analysis complete.';
                                    renderImportBreakdown(data);
                                } else if (attempts < maxAttempts) {
                                    setTimeout(poll, 1000);
                                } else {
                                    statusEl.textContent = 'Timed out waiting for analysis.';
                                }
                            });
                        }
                        setTimeout(poll, 1000);
                    }).catch(function(err){
                        console.error('Import analyze failed', err);
                        statusEl.textContent = 'Import failed.';
                        Dashboard.alert({ title: 'Import', message: 'Upload/analyze failed. See console.' });
                    });
                } catch (e) {
                    console.error('Failed to encode file', e);
                    statusEl.textContent = 'Import failed.';
                }
            };
            reader.readAsArrayBuffer(file);
        });
    }

    function esc(s){ return (s||'').toString().replace(/[&<>\"']/g, function(c){ return ({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',"'":'&#39;'})[c]; }); }
    function renderImportBreakdown(data){
        var breakdownEl = document.getElementById('ImportBreakdown');
        if (!breakdownEl) return;
        var users = (data && data.Users) || [];
        var libs = (data && data.Libraries) || [];
        var errs = (data && data.Errors) || [];
        var extracted = (data && data.ExtractedPath) || '';

        var html = '';
        html += '<div style="font-weight:600; margin-bottom:6px;">Import Analysis</div>';
        if (extracted) { html += '<div class="fieldDescription">Extracted to: ' + esc(extracted) + '</div>'; }
        if (errs.length){ html += '<div style="color:#e67e22; margin:6px 0;">Warnings/Errors:<ul>' + errs.map(function(e){return '<li>'+esc(e)+'</li>';}).join('') + '</ul></div>'; }
        html += '<div style="margin-top:6px;">Users found: <b>' + users.length + '</b></div>';
        if (users.length){
            var showU = users.slice(0, 20).map(function(u){ return esc(((u.Username||u.username||'') + (u.Id? ' ['+u.Id+']':''))); });
            html += '<div class="fieldDescription">' + showU.join(', ') + (users.length>20?' ...':'') + '</div>';
        }
        html += '<div style="margin-top:6px;">Libraries found: <b>' + libs.length + '</b></div>';
        if (libs.length){
            var showL = libs.slice(0, 20).map(function(l){ return esc(((l.Name||l.name||'') + (l.Id? ' ['+l.Id+']':''))); });
            html += '<div class="fieldDescription">' + showL.join(', ') + (libs.length>20?' ...':'') + '</div>';
        }
        breakdownEl.innerHTML = html;
        breakdownEl.style.display = 'block';
    }

    // Local save and ZIP download actions
    var serverZipBtn = document.getElementById('DownloadServerZipBtn');
    if (serverZipBtn) serverZipBtn.addEventListener('click', function(){
        ApiClient.getPluginConfiguration(TemplateConfig.pluginUniqueId).then(downloadServerZipFromConfig);
    });
})();
