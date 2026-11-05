import React, { createContext, useContext, useState, useCallback } from 'react';
import { Snackbar, Alert, AlertColor, Button } from '@mui/material';

export interface AppNotificationAction {
  label: string;
  onClick: () => void;
}

export interface AppNotification {
  id: string;
  message: string;
  type: AlertColor;
  durationMs?: number;
  action?: AppNotificationAction;
}

interface NotificationContextType {
  showNotification: (
    message: string,
    type?: AlertColor,
    options?: { durationMs?: number; action?: AppNotificationAction }
  ) => void;
  notifySuccess: (message: string, action?: AppNotificationAction) => void;
  notifyInfo: (message: string, action?: AppNotificationAction) => void;
  notifyWarning: (message: string, action?: AppNotificationAction) => void;
  notifyError: (message: string, action?: AppNotificationAction) => void;
}

const NotificationContext = createContext<NotificationContextType | null>(null);

export const NotificationProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [currentNotification, setCurrentNotification] = useState<AppNotification | null>(null);
  const [isOpen, setIsOpen] = useState(false);

  const showNotification = useCallback(
    (
      message: string,
      type: AlertColor = 'info',
      options?: { durationMs?: number; action?: AppNotificationAction }
    ) => {
      setCurrentNotification({
        id: `notif_${Date.now()}_${Math.random()}`,
        message,
        type,
        durationMs: options?.durationMs || 3500,
        action: options?.action
      });
      setIsOpen(true);
    },
    []
  );

  const notifySuccess = useCallback(
    (message: string, action?: AppNotificationAction) => showNotification(message, 'success', { action }),
    [showNotification]
  );

  const notifyInfo = useCallback(
    (message: string, action?: AppNotificationAction) => showNotification(message, 'info', { action }),
    [showNotification]
  );

  const notifyWarning = useCallback(
    (message: string, action?: AppNotificationAction) => showNotification(message, 'warning', { action }),
    [showNotification]
  );

  const notifyError = useCallback(
    (message: string, action?: AppNotificationAction) => showNotification(message, 'error', { action, durationMs: 5000 }),
    [showNotification]
  );

  const handleClose = (_event?: React.SyntheticEvent | Event, reason?: string) => {
    if (reason === 'clickaway') return;
    setIsOpen(false);
  };

  return (
    <NotificationContext.Provider
      value={{
        showNotification,
        notifySuccess,
        notifyInfo,
        notifyWarning,
        notifyError
      }}
    >
      {children}
      <Snackbar
        open={isOpen}
        autoHideDuration={currentNotification?.durationMs || 3500}
        onClose={handleClose}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        sx={{ mb: 2 }}
      >
        {currentNotification ? (
          <Alert
            role={currentNotification.type === 'error' ? 'alert' : 'status'}
            aria-live={currentNotification.type === 'error' ? 'assertive' : 'polite'}
            onClose={handleClose}
            severity={currentNotification.type}
            variant="filled"
            action={
              currentNotification.action ? (
                <Button
                  color="inherit"
                  size="small"
                  onClick={() => {
                    currentNotification.action?.onClick();
                    setIsOpen(false);
                  }}
                  sx={{ fontWeight: 800, textTransform: 'uppercase', fontSize: '0.72rem' }}
                >
                  {currentNotification.action.label}
                </Button>
              ) : undefined
            }
            sx={{
              width: '100%',
              boxShadow: '0 6px 20px rgba(0,0,0,0.4)',
              fontWeight: 600,
              fontSize: '0.82rem',
              alignItems: 'center'
            }}
          >
            {currentNotification.message}
          </Alert>
        ) : undefined}
      </Snackbar>
    </NotificationContext.Provider>
  );
};

export const useNotification = () => {
  const context = useContext(NotificationContext);
  if (!context) throw new Error('useNotification must be used within a NotificationProvider');
  return context;
};
