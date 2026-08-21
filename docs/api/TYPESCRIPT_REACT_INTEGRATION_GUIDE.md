# GymFlowPro API Integration Guide for Web (TypeScript/React)

## Setup

### 1. Install Dependencies

```bash
npm install axios react-query zustand crypto-js
# or
yarn add axios react-query zustand crypto-js
```

### 2. Create API Client

```typescript
// src/services/api.client.ts
import axios, { AxiosInstance, AxiosError } from 'axios';
import { useAuthStore } from '../store/auth.store';

export class ApiClient {
  private client: AxiosInstance;
  private baseURL = process.env.REACT_APP_API_URL || 'http://localhost:5000/api';

  constructor() {
    this.client = axios.create({
      baseURL: this.baseURL,
      headers: {
        'Content-Type': 'application/json',
      },
    });

    // Request interceptor to add token
    this.client.interceptors.request.use((config) => {
      const token = useAuthStore.getState().accessToken;
      if (token) {
        config.headers.Authorization = `Bearer ${token}`;
      }
      return config;
    });

    // Response interceptor to handle token expiry
    this.client.interceptors.response.use(
      (response) => response,
      async (error: AxiosError) => {
        const originalRequest = error.config as any;

        if (error.response?.status === 401 && !originalRequest._retry) {
          originalRequest._retry = true;

          try {
            const refreshToken = useAuthStore.getState().refreshToken;
            const response = await this.refresh(refreshToken);
            
            useAuthStore.getState().setTokens(
              response.accessToken,
              response.refreshToken
            );

            originalRequest.headers.Authorization = `Bearer YOUR_ACCESS_TOKEN`;
            return this.client(originalRequest);
          } catch (refreshError) {
            useAuthStore.getState().logout();
            window.location.href = '/login';
            return Promise.reject(refreshError);
          }
        }

        return Promise.reject(error);
      }
    );
  }

  // Auth Endpoints
  async staffLogin(email: string, password: string) {
    const response = await this.client.post('/auth/login', { email, password });
    return response.data;
  }

  async memberSendOtp(phoneNumber: string) {
    const response = await this.client.post('/auth/member-otp', { phoneNumber });
    return response.data;
  }

  async memberVerifyOtp(phoneNumber: string, otp: string) {
    const response = await this.client.post('/auth/member-verify', { 
      phoneNumber, 
      otp 
    });
    return response.data;
  }

  async refresh(refreshToken: string) {
    const response = await this.client.post('/auth/refresh', { refreshToken });
    return response.data;
  }

  // Members Endpoints
  async getMembers(page = 1, pageSize = 20, search?: string, status?: string) {
    const params = new URLSearchParams();
    params.set('page', page.toString());
    params.set('pageSize', pageSize.toString());
    if (search) params.set('search', search);
    if (status) params.set('status', status);

    const response = await this.client.get(`/members?${params}`);
    return response.data;
  }

  async getMemberById(id: string) {
    const response = await this.client.get(`/members/${id}`);
    return response.data;
  }

  async createMember(data: CreateMemberRequest) {
    const response = await this.client.post('/members', data);
    return response.data;
  }

  async updateMember(id: string, data: Partial<CreateMemberRequest>) {
    const response = await this.client.put(`/members/${id}`, data);
    return response.data;
  }

  async getMemberAttendance(id: string, page = 1, pageSize = 20) {
    const response = await this.client.get(
      `/members/${id}/attendance?page=${page}&pageSize=${pageSize}`
    );
    return response.data;
  }

  // Attendance Endpoints
  async qrCheckin(qrToken: string) {
    const response = await this.client.post('/attendance/qr-checkin', { qrToken });
    return response.data;
  }

  async manualCheckin(data: ManualCheckinRequest) {
    const response = await this.client.post('/attendance/manual-checkin', data);
    return response.data;
  }

  async searchMembers(search: string) {
    const response = await this.client.get(`/attendance/search-members?search=${search}`);
    return response.data;
  }

  async getTodayAttendance(filter = 'all') {
    const response = await this.client.get(`/attendance/today?filter=${filter}`);
    return response.data;
  }

  // Plans Endpoints
  async getPlans() {
    const response = await this.client.get('/membership-plans');
    return response.data;
  }

  async getPlanById(id: string) {
    const response = await this.client.get(`/membership-plans/${id}`);
    return response.data;
  }

  async createPlan(data: CreatePlanRequest) {
    const response = await this.client.post('/membership-plans', data);
    return response.data;
  }

  async updatePlan(id: string, data: Partial<CreatePlanRequest>) {
    const response = await this.client.put(`/membership-plans/${id}`, data);
    return response.data;
  }

  // Memberships Endpoints
  async assignMembership(data: AssignMembershipRequest) {
    const response = await this.client.post('/memberships/assign', data);
    return response.data;
  }

  async renewMembership(id: string, data: RenewMembershipRequest) {
    const response = await this.client.post(`/memberships/${id}/renew`, data);
    return response.data;
  }

  // Analytics Endpoints
  async getDashboardOverview() {
    const response = await this.client.get('/analytics/dashboard-overview');
    return response.data;
  }

  async getRevenueChart(months = 6) {
    const response = await this.client.get(`/analytics/revenue-chart?months=${months}`);
    return response.data;
  }

  async getAttendanceHeatmap() {
    const response = await this.client.get('/analytics/attendance-heatmap');
    return response.data;
  }
}

export const apiClient = new ApiClient();

// Type Definitions
export interface CreateMemberRequest {
  firstName: string;
  lastName: string;
  phoneNumber: string;
  email: string;
  dateOfBirth?: string;
  gender?: string;
  emergencyContact?: string;
  emergencyPhone?: string;
}

export interface ManualCheckinRequest {
  memberId: string;
  reason: 0 | 1 | 2 | 3 | 4;
  notes?: string;
}

export interface CreatePlanRequest {
  name: string;
  type: 'monthly_unlimited' | 'session_pack' | 'time_limited' | 'pt_credits' | 'family';
  price: number;
  durationDays?: number;
  sessionCount?: number;
  description?: string;
}

export interface AssignMembershipRequest {
  memberId: string;
  planId: string;
  startDate: string;
  paymentMethod: string;
  notes?: string;
}

export interface RenewMembershipRequest {
  planId: string;
  paymentMethod: string;
  notes?: string;
}
```

---

## State Management with Zustand

```typescript
// src/store/auth.store.ts
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  role: string;
  gymCode: string;
}

interface AuthStore {
  user: User | null;
  accessToken: string | null;
  refreshToken: string | null;
  isAuthenticated: boolean;

  setTokens: (accessToken: string, refreshToken: string) => void;
  setUser: (user: User) => void;
  logout: () => void;
  isTokenExpired: () => boolean;
}

export const useAuthStore = create<AuthStore>()(
  persist(
    (set, get) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,

      setTokens: (accessToken, refreshToken) => {
        set({ accessToken, refreshToken, isAuthenticated: true });
      },

      setUser: (user) => {
        set({ user });
      },

      logout: () => {
        set({
          user: null,
          accessToken: null,
          refreshToken: null,
          isAuthenticated: false,
        });
      },

      isTokenExpired: () => {
        const token = get().accessToken;
        if (!token) return true;

        try {
          // Decode JWT without verification (client-side only)
          const decoded = JSON.parse(atob(token.split('.')[1]));
          return decoded.exp * 1000 < Date.now();
        } catch {
          return true;
        }
      },
    }),
    {
      name: 'auth-storage',
      partialize: (state) => ({
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        user: state.user,
      }),
    }
  )
);
```

---

## React Hooks

```typescript
// src/hooks/useAuth.ts
import { useQuery, useMutation } from 'react-query';
import { apiClient } from '../services/api.client';
import { useAuthStore } from '../store/auth.store';

export const useStaffLogin = () => {
  const { setTokens, setUser } = useAuthStore();

  return useMutation(
    (credentials: { email: string; password: string }) =>
      apiClient.staffLogin(credentials.email, credentials.password),
    {
      onSuccess: (data) => {
        setTokens(data.accessToken, data.refreshToken);
        setUser(data.user);
      },
    }
  );
};

export const useMemberOtpSend = () => {
  return useMutation((phoneNumber: string) =>
    apiClient.memberSendOtp(phoneNumber)
  );
};

export const useMemberOtpVerify = () => {
  const { setTokens, setUser } = useAuthStore();

  return useMutation(
    (params: { phoneNumber: string; otp: string }) =>
      apiClient.memberVerifyOtp(params.phoneNumber, params.otp),
    {
      onSuccess: (data) => {
        setTokens(data.accessToken, data.refreshToken);
        setUser(data.user);
      },
    }
  );
};

// src/hooks/useMembers.ts
export const useMembers = (page = 1, pageSize = 20, search?: string, status?: string) => {
  return useQuery(
    ['members', page, pageSize, search, status],
    () => apiClient.getMembers(page, pageSize, search, status),
    { staleTime: 1000 * 60 * 5 } // 5 minutes
  );
};

export const useMemberById = (id: string) => {
  return useQuery(
    ['member', id],
    () => apiClient.getMemberById(id),
    { staleTime: 1000 * 60 * 5 }
  );
};

export const useCreateMember = () => {
  const queryClient = useQueryClient();
  return useMutation((data: CreateMemberRequest) => apiClient.createMember(data), {
    onSuccess: () => {
      queryClient.invalidateQueries('members');
    },
  });
};

// src/hooks/useAttendance.ts
export const useQrCheckin = () => {
  return useMutation((qrToken: string) => apiClient.qrCheckin(qrToken));
};

export const useManualCheckin = () => {
  const queryClient = useQueryClient();
  return useMutation((data: ManualCheckinRequest) => apiClient.manualCheckin(data), {
    onSuccess: () => {
      queryClient.invalidateQueries('attendance');
    },
  });
};

export const useTodayAttendance = (filter = 'all') => {
  return useQuery(
    ['attendance', 'today', filter],
    () => apiClient.getTodayAttendance(filter),
    { refetchInterval: 1000 * 10 } // Refresh every 10 seconds
  );
};

// src/hooks/useAnalytics.ts
export const useDashboardOverview = () => {
  return useQuery(
    ['analytics', 'dashboard'],
    () => apiClient.getDashboardOverview(),
    { staleTime: 1000 * 60 * 5, refetchInterval: 1000 * 60 * 2 } // 5 min cache, 2 min refetch
  );
};

export const useRevenueChart = (months = 6) => {
  return useQuery(
    ['analytics', 'revenue', months],
    () => apiClient.getRevenueChart(months),
    { staleTime: 1000 * 60 * 5 }
  );
};
```

---

## React Components

### Login Component

```typescript
// src/components/StaffLogin.tsx
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useStaffLogin } from '../hooks/useAuth';

export const StaffLogin: React.FC = () => {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const navigate = useNavigate();
  const { mutate: login, isLoading, error } = useStaffLogin();

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    login(
      { email, password },
      {
        onSuccess: () => navigate('/dashboard'),
      }
    );
  };

  return (
    <div className="login-container">
      <form onSubmit={handleSubmit}>
        <h2>Staff Login</h2>
        
        <div className="form-group">
          <label htmlFor="email">Email:</label>
          <input
            id="email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </div>

        <div className="form-group">
          <label htmlFor="password">Password:</label>
          <input
            id="password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </div>

        {error && (
          <div className="error-message">
            {error instanceof Error ? error.message : 'Login failed'}
          </div>
        )}

        <button type="submit" disabled={isLoading}>
          {isLoading ? 'Logging in...' : 'Login'}
        </button>
      </form>
    </div>
  );
};
```

### Member List Component

```typescript
// src/components/MemberList.tsx
import React, { useState } from 'react';
import { useMembers } from '../hooks/useMembers';
import { useNavigate } from 'react-router-dom';

export const MemberList: React.FC = () => {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<string>();
  const navigate = useNavigate();

  const { data, isLoading, error } = useMembers(page, 20, search, status);

  if (isLoading) return <div>Loading...</div>;
  if (error) return <div>Error: {error.toString()}</div>;

  return (
    <div className="member-list">
      <h2>Members</h2>

      <div className="filters">
        <input
          type="text"
          placeholder="Search member..."
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
        />

        <select
          value={status || ''}
          onChange={(e) => {
            setStatus(e.target.value || undefined);
            setPage(1);
          }}
        >
          <option value="">All Status</option>
          <option value="active">Active</option>
          <option value="expired">Expired</option>
          <option value="frozen">Frozen</option>
        </select>
      </div>

      <table>
        <thead>
          <tr>
            <th>Member #</th>
            <th>Name</th>
            <th>Email</th>
            <th>Status</th>
            <th>Plan</th>
            <th>Expiry</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {data?.data?.map((member: any) => (
            <tr key={member.id}>
              <td>{member.memberNumber}</td>
              <td>{member.firstName} {member.lastName}</td>
              <td>{member.email}</td>
              <td>
                <span className={`status-badge status-${member.status}`}>
                  {member.status}
                </span>
              </td>
              <td>{member.planName}</td>
              <td>{new Date(member.expiryDate).toLocaleDateString()}</td>
              <td>
                <button onClick={() => navigate(`/members/${member.id}`)}>
                  View
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Pagination */}
      <div className="pagination">
        <button
          disabled={page === 1}
          onClick={() => setPage(page - 1)}
        >
          Previous
        </button>
        <span>{page}</span>
        <button
          disabled={!data?.data || data.data.length < 20}
          onClick={() => setPage(page + 1)}
        >
          Next
        </button>
      </div>
    </div>
  );
};
```

### Dashboard Component

```typescript
// src/components/Dashboard.tsx
import React from 'react';
import { useDashboardOverview } from '../hooks/useAnalytics';
import { PieChart, BarChart } from 'react-charts';

export const Dashboard: React.FC = () => {
  const { data, isLoading } = useDashboardOverview();

  if (isLoading) return <div>Loading dashboard...</div>;

  return (
    <div className="dashboard">
      <h2>Dashboard Overview</h2>

      <div className="metrics-grid">
        <MetricCard
          title="Active Members"
          value={data?.activeMembers}
          icon="👥"
        />
        <MetricCard
          title="Expired Members"
          value={data?.expiredMembers}
          icon="⏰"
        />
        <MetricCard
          title="New This Month"
          value={data?.newMembersThisMonth}
          icon="✨"
        />
        <MetricCard
          title="Revenue This Month"
          value={`EGP ${data?.revenueThisMonth?.toLocaleString()}`}
          icon="💰"
        />
        <MetricCard
          title="Check-ins Today"
          value={data?.checkinsToday}
          icon="📍"
        />
        <MetricCard
          title="Check-ins This Week"
          value={data?.checkinsThisWeek}
          icon="📊"
        />
      </div>

      <div className="last-updated">
        Last updated: {new Date(data?.snapshotTimeUtc).toLocaleString()}
      </div>
    </div>
  );
};

const MetricCard: React.FC<{ title: string; value: any; icon: string }> = ({
  title,
  value,
  icon,
}) => (
  <div className="metric-card">
    <span className="icon">{icon}</span>
    <p className="metric-title">{title}</p>
    <p className="metric-value">{value}</p>
  </div>
);
```

### Manual Check-in Component

```typescript
// src/components/ManualCheckin.tsx
import React, { useState } from 'react';
import { useManualCheckin, useSearchMembers } from '../hooks/useAttendance';
import { useMembers } from '../hooks/useMembers';

export const ManualCheckin: React.FC = () => {
  const [memberId, setMemberId] = useState('');
  const [searchTerm, setSearchTerm] = useState('');
  const [reason, setReason] = useState<0 | 1 | 2 | 3 | 4>(1);
  const [notes, setNotes] = useState('');

  const { mutate: checkin, isLoading, error, isSuccess } = useManualCheckin();
  const { data: searchResults } = useSearchMembers(searchTerm);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!memberId) {
      alert('Please select a member');
      return;
    }

    checkin({ memberId, reason, notes });
  };

  const reasonLabels = [
    'Guest Check-in',
    'Forgot QR Code',
    'System Error',
    'Special Authorization',
    'Other',
  ];

  return (
    <div className="manual-checkin-form">
      <h2>Manual Check-in</h2>

      <form onSubmit={handleSubmit}>
        {/* Member Search */}
        <div className="form-group">
          <label htmlFor="search">Search Member:</label>
          <input
            id="search"
            type="text"
            placeholder="Name or Member #"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            minLength={2}
          />

          {searchResults && searchResults.length > 0 && (
            <div className="search-results">
              {searchResults.map((member: any) => (
                <div
                  key={member.id}
                  className={`search-result ${member.selectable ? '' : 'disabled'}`}
                  onClick={() => {
                    if (member.selectable) {
                      setMemberId(member.id);
                      setSearchTerm('');
                    }
                  }}
                >
                  <span>{member.memberNumber} - {member.firstName} {member.lastName}</span>
                  {!member.selectable && <span className="reason">({member.reason})</span>}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Selected Member */}
        {memberId && (
          <div className="selected-member">
            Selected: {searchResults?.find((m: any) => m.id === memberId)?.memberNumber}
          </div>
        )}

        {/* Reason */}
        <div className="form-group">
          <label htmlFor="reason">Reason:</label>
          <select value={reason} onChange={(e) => setReason(parseInt(e.target.value) as any)}>
            {reasonLabels.map((label, idx) => (
              <option key={idx} value={idx}>
                {label}
              </option>
            ))}
          </select>
        </div>

        {/* Notes */}
        <div className="form-group">
          <label htmlFor="notes">Notes:</label>
          <textarea
            id="notes"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            placeholder="Optional notes..."
          />
        </div>

        {error && (
          <div className="error-message">
            {error instanceof Error ? error.message : 'Check-in failed'}
          </div>
        )}

        {isSuccess && (
          <div className="success-message">
            ✓ Member checked in successfully!
          </div>
        )}

        <button type="submit" disabled={isLoading || !memberId}>
          {isLoading ? 'Processing...' : 'Check-in Member'}
        </button>
      </form>
    </div>
  );
};
```

---

## Error Handling

```typescript
// src/utils/errorHandler.ts
import { AxiosError } from 'axios';

export interface ApiErrorResponse {
  error: string;
  message?: string;
  details?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    public status: number,
    public data: ApiErrorResponse,
    message?: string
  ) {
    super(message || data.error);
  }
}

export const handleApiError = (error: unknown): string => {
  if (error instanceof AxiosError) {
    const response = error.response?.data as ApiErrorResponse;

    if (error.response?.status === 401) {
      return 'Your session has expired. Please log in again.';
    }

    if (error.response?.status === 403) {
      return 'You do not have permission to perform this action.';
    }

    if (error.response?.status === 429) {
      return 'Too many requests. Please wait and try again.';
    }

    if (error.response?.status === 400) {
      const firstError = Object.values(response.details || {})[0]?.[0];
      return firstError || response.error || 'Invalid request.';
    }

    if (error.response?.status === 404) {
      return 'Resource not found.';
    }

    if (error.response?.status === 500) {
      return 'Server error. Please try again later.';
    }
  }

  if (error instanceof Error) {
    return error.message;
  }

  return 'An unexpected error occurred.';
};
```

---

## Environment Configuration

```typescript
// src/config/api.config.ts
export const apiConfig = {
  development: {
    baseUrl: 'http://localhost:5000/api',
    timeout: 30000,
  },
  staging: {
    baseUrl: 'https://staging-api.gymflowpro.test/api',
    timeout: 30000,
  },
  production: {
    baseUrl: 'https://api.gymflowpro.test/api',
    timeout: 30000,
  },
};

export const getApiConfig = () => {
  const env = process.env.REACT_APP_ENV || 'development';
  return apiConfig[env as keyof typeof apiConfig];
};
```

---

## Testing Examples

```typescript
// src/__tests__/api.client.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { apiClient } from '../services/api.client';

describe('ApiClient', () => {
  describe('staffLogin', () => {
    it('should return tokens and user data on successful login', async () => {
      const result = await apiClient.staffLogin(
        'test@gymflow.test',
        'password123'
      );

      expect(result).toHaveProperty('accessToken');
      expect(result).toHaveProperty('refreshToken');
      expect(result.user).toHaveProperty('id');
      expect(result.user).toHaveProperty('email');
    });

    it('should throw on invalid credentials', async () => {
      expect(
        apiClient.staffLogin('invalid@test.com', 'wrongpass')
      ).rejects.toThrow();
    });
  });

  describe('getMembers', () => {
    it('should return paginated members list', async () => {
      const result = await apiClient.getMembers();

      expect(result).toHaveProperty('data');
      expect(result).toHaveProperty('pageNumber');
      expect(result).toHaveProperty('totalCount');
      expect(Array.isArray(result.data)).toBe(true);
    });

    it('should filter by search term', async () => {
      const result = await apiClient.getMembers(1, 20, 'ahmed');

      expect(result.data.length).toBeGreaterThan(0);
      expect(result.data[0]).toHaveProperty('firstName');
    });
  });
});
```

---

## Performance Optimization

```typescript
// src/utils/queryClient.ts
import { QueryClient } from 'react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Cache for 5 minutes
      staleTime: 1000 * 60 * 5,
      // Keep in memory for 10 minutes
      cacheTime: 1000 * 60 * 10,
      // Retry failed requests once
      retry: 1,
      // Don't refetch on window focus
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: 1,
    },
  },
});
```

---

## Resources

- **Axios Documentation**: https://axios-http.com/
- **React Query Documentation**: https://react-query-v3.tanstack.com/
- **Zustand Documentation**: https://github.com/pmndrs/zustand
- **TypeScript Handbook**: https://www.typescriptlang.org/docs/

---

**Last Updated**: 2025-05-30

