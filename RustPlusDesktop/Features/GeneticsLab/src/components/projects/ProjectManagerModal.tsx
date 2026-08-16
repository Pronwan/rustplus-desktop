import React, { useState } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  TextField,
  Tabs,
  Tab,
  IconButton,
  Chip,
  Paper
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import SaveIcon from '@mui/icons-material/Save';
import FileDownloadIcon from '@mui/icons-material/FileDownload';
import FileUploadIcon from '@mui/icons-material/FileUpload';
import DeleteIcon from '@mui/icons-material/Delete';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { useNotification } from '../../context/NotificationContext.tsx';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';

interface ProjectManagerModalProps {
  open: boolean;
  onClose: () => void;
}

export const ProjectManagerModal: React.FC<ProjectManagerModalProps> = ({ open, onClose }) => {
  const {
    projects,
    saveCurrentAsProject,
    loadProject,
    deleteProject,
    exportWorkspaceJson,
    importWorkspaceJson,
    selectedPlant,
    clones
  } = useWorkspace();
  const { notifySuccess, notifyError } = useNotification();

  const [tab, setTab] = useState<'saved' | 'save_current' | 'import_export'>('saved');
  const [newProjectName, setNewProjectName] = useState('');
  const [importJsonText, setImportJsonText] = useState('');

  const handleSave = () => {
    saveCurrentAsProject(newProjectName);
    setNewProjectName('');
    setTab('saved');
  };

  const handleCopyJson = () => {
    const json = exportWorkspaceJson();
    navigator.clipboard.writeText(json);
    notifySuccess('Copied workspace JSON to clipboard');
  };

  const handleImport = () => {
    const res = importWorkspaceJson(importJsonText);
    if (res.success) {
      setImportJsonText('');
      onClose();
    } else {
      notifyError(res.error || 'Import failed');
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="sm"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: 'var(--gl-panel-bg)',
            border: '1px solid var(--gl-surface-hover)',
            borderRadius: '6px',
            color: 'var(--gl-text-primary)'
          }
        }
      }}
    >
      <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
          Farm Projects & Data Manager
        </Typography>
        <IconButton size="small" onClick={onClose} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <Box sx={{ borderBottom: '1px solid var(--gl-border)', px: 3 }}>
        <Tabs
          value={tab}
          onChange={(_, val) => setTab(val)}
          sx={{ minHeight: 36, '& .MuiTabs-indicator': { backgroundColor: 'var(--gl-primary)' } }}
        >
          <Tab value="saved" label={`Saved Farms (${projects.length})`} sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
          <Tab value="save_current" label="Save Current" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
          <Tab value="import_export" label="Import / Export JSON" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
        </Tabs>
      </Box>

      <DialogContent sx={{ pt: 2.5, minHeight: 280 }}>
        {tab === 'saved' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            {projects.length === 0 ? (
              <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', textAlign: 'center', py: 4 }}>
                No saved farm projects yet. Save your current setup to quickly switch between crops.
              </Typography>
            ) : (
              projects.map((proj) => (
                <Paper
                  key={proj.id}
                  variant="outlined"
                  sx={{
                    p: 1.5,
                    backgroundColor: 'var(--gl-input-bg)',
                    borderColor: 'var(--gl-border)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between'
                  }}
                >
                  <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                      <Typography variant="subtitle2" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
                        {proj.name}
                      </Typography>
                      <Chip
                        size="small"
                        label={proj.cropType.replace(/-/g, ' ')}
                        sx={{ height: 18, fontSize: '0.65rem', backgroundColor: 'var(--gl-surface)', color: 'var(--gl-text-secondary)' }}
                      />
                    </Box>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                      <GeneticsSequence genes={proj.targetGenetics} size="small" />
                      <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
                        {proj.clones.length} Clones
                      </Typography>
                    </Box>
                  </Box>

                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Button
                      size="small"
                      variant="contained"
                      onClick={() => {
                        loadProject(proj.id);
                        onClose();
                      }}
                      sx={{ fontSize: '0.72rem', py: 0.3 }}
                    >
                      Load
                    </Button>
                    <IconButton size="small" onClick={() => deleteProject(proj.id)} sx={{ color: 'var(--gl-error)' }}>
                      <DeleteIcon sx={{ fontSize: 16 }} />
                    </IconButton>
                  </Box>
                </Paper>
              ))
            )}
          </Box>
        )}

        {tab === 'save_current' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>
              Save your current {clones.length} clones and target for {selectedPlant.replace(/-/g, ' ')}:
            </Typography>

            <TextField
              size="small"
              fullWidth
              placeholder="e.g. Wipe Day 2 Hemp Farm"
              value={newProjectName}
              onChange={(e) => setNewProjectName(e.target.value)}
              autoFocus
            />

            <Button
              variant="contained"
              size="small"
              onClick={handleSave}
              startIcon={<SaveIcon sx={{ fontSize: 16 }} />}
              sx={{ alignSelf: 'flex-start', fontWeight: 800 }}
            >
              Save Project
            </Button>
          </Box>
        )}

        {tab === 'import_export' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <Box>
              <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, textTransform: 'uppercase', mb: 1, display: 'block' }}>
                EXPORT CURRENT WORKSPACE
              </Typography>
              <Button
                variant="outlined"
                size="small"
                onClick={handleCopyJson}
                startIcon={<ContentCopyIcon sx={{ fontSize: 15 }} />}
                sx={{ borderColor: 'var(--gl-border-strong)', color: 'var(--gl-text-primary)' }}
              >
                Copy Workspace JSON
              </Button>
            </Box>

            <Box>
              <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, textTransform: 'uppercase', mb: 1, display: 'block' }}>
                IMPORT WORKSPACE JSON
              </Typography>
              <TextField
                multiline
                rows={4}
                fullWidth
                placeholder="Paste workspace JSON here..."
                value={importJsonText}
                onChange={(e) => setImportJsonText(e.target.value)}
                sx={{ mb: 1 }}
              />
              <Button
                variant="contained"
                size="small"
                disabled={!importJsonText.trim()}
                onClick={handleImport}
                startIcon={<FileUploadIcon sx={{ fontSize: 16 }} />}
                sx={{ fontWeight: 800 }}
              >
                Import JSON
              </Button>
            </Box>
          </Box>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose} variant="contained" size="small">
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
};
