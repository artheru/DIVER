/**
 * @file api/index.ts
 * @description API 请求封装 - 统一的 HTTP 客户端配置
 * 
 * 关键设计：
 * 1. 统一错误处理
 * 2. 请求/响应拦截器
 * 3. 类型安全的请求方法
 */

import axios, { AxiosError, type AxiosRequestConfig } from 'axios'

/**
 * 创建 Axios 实例，配置基础 URL 和超时
 */
const http = axios.create({
  baseURL: '/',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json'
  }
})

/**
 * 请求拦截器 - 可在此添加认证 token 等
 */
http.interceptors.request.use(
  (config) => {
    // 如果需要认证，可以在这里添加 token
    // config.headers.Authorization = `Bearer ${token}`
    return config
  },
  (error) => {
    return Promise.reject(error)
  }
)

/**
 * 响应拦截器 - 统一处理错误
 */
http.interceptors.response.use(
  (response) => {
    return response
  },
  (error: AxiosError<{ error?: string }>) => {
    // 提取后端返回的错误信息
    const message = error.response?.data?.error 
      || error.message 
      || 'Unknown error'
    
    console.error(`[API Error] ${error.config?.method?.toUpperCase()} ${error.config?.url}: ${message}`)
    
    return Promise.reject(new Error(message))
  }
)

/**
 * 通用 GET 请求
 */
export async function get<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
  const response = await http.get<T>(url, config)
  return response.data
}

/**
 * 通用 POST 请求
 */
export async function post<T>(url: string, data?: unknown, config?: AxiosRequestConfig): Promise<T> {
  const response = await http.post<T>(url, data, config)
  return response.data
}

/**
 * 通用 DELETE 请求
 */
export async function del<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
  const response = await http.delete<T>(url, config)
  return response.data
}

/**
 * 上传文件 (multipart/form-data)
 */
export async function upload<T>(url: string, file: File, fieldName = 'file'): Promise<T> {
  const formData = new FormData()
  formData.append(fieldName, file)
  
  const response = await http.post<T>(url, formData, {
    headers: {
      'Content-Type': 'multipart/form-data'
    }
  })
  return response.data
}

export default http
